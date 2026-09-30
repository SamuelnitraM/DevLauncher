using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DevLauncher.Views.Behaviors;

/// <summary>
/// Keeps the last item of a ListBox visible. The displayed items are listened to, not the source collection :
/// they notify once the list has taken the change into account, and the scroll runs after the pending layout.
/// </summary>
public static class ListBoxAutoScroll
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(ListBoxAutoScroll), new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly ConditionalWeakTable<ListBox, NotifyCollectionChangedEventHandler> _itemsChangedHandlers = new();
    private static readonly ConditionalWeakTable<ListBox, object> _pendingScrolls = new();

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not ListBox listBox) return;
        var displayedItems = (INotifyCollectionChanged)listBox.Items;
        if (_itemsChangedHandlers.TryGetValue(listBox, out var previousHandler))
        {
            displayedItems.CollectionChanged -= previousHandler;
            _itemsChangedHandlers.Remove(listBox);
        }
        if (e.NewValue is not true) return;
        NotifyCollectionChangedEventHandler itemsChangedHandler = (_, changeArgs) =>
        {
            if (changeArgs.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Reset) ScheduleScrollToEnd(listBox);
        };
        displayedItems.CollectionChanged += itemsChangedHandler;
        _itemsChangedHandlers.Add(listBox, itemsChangedHandler);
    }

    /// <summary>Queues a single scroll for a burst of changes.</summary>
    private static void ScheduleScrollToEnd(ListBox listBox)
    {
        if (_pendingScrolls.TryGetValue(listBox, out _)) return;
        _pendingScrolls.Add(listBox, new object());
        listBox.Dispatcher.BeginInvoke(() =>
        {
            _pendingScrolls.Remove(listBox);
            if (listBox.Items.Count > 0) listBox.ScrollIntoView(listBox.Items[listBox.Items.Count - 1]);
        }, DispatcherPriority.Background);
    }
}
