using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using DevLauncher.ViewModels;

namespace DevLauncher.Views;

/// <summary>Main window. Holds only view behavior : keeping the selected project and the last log line visible.</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel previousViewModel) previousViewModel.LogEntries.CollectionChanged -= OnLogEntriesChanged;
        if (e.NewValue is MainViewModel mainViewModel) mainViewModel.LogEntries.CollectionChanged += OnLogEntriesChanged;
    }

    private void OnLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || LogListBox.Items.Count == 0) return;
        LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
    }

    private void ProjectListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectListBox.SelectedItem is not null) ProjectListBox.ScrollIntoView(ProjectListBox.SelectedItem);
    }
}
