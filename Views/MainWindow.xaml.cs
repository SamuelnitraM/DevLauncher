using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DevLauncher.Views;

/// <summary>Main window. Holds only view behavior : keeping the selected project and the last log line visible.</summary>
public partial class MainWindow : Window
{
    private bool _isLogScrollPending;

    public MainWindow()
    {
        InitializeComponent();
        // The displayed items are listened to, not the source collection : they notify once the list has taken the change into account.
        ((INotifyCollectionChanged)LogListBox.Items).CollectionChanged += OnLogItemsChanged;
    }

    /// <summary>Scrolls to the last log line once the pending layout is done, a burst of lines producing a single scroll.</summary>
    private void OnLogItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || _isLogScrollPending) return;
        _isLogScrollPending = true;
        Dispatcher.BeginInvoke(ScrollLogToEnd, DispatcherPriority.Background);
    }

    private void ScrollLogToEnd()
    {
        _isLogScrollPending = false;
        if (LogListBox.Items.Count > 0) LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
    }

    private void ProjectListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectListBox.SelectedItem is null) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (ProjectListBox.SelectedItem is not null) ProjectListBox.ScrollIntoView(ProjectListBox.SelectedItem);
        }, DispatcherPriority.Background);
    }
}
