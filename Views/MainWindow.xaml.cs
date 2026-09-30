using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DevLauncher.ViewModels;

namespace DevLauncher.Views;

/// <summary>Main window. Holds only view behavior : initial size, closing confirmation and keeping the selected project visible.</summary>
public partial class MainWindow : Window
{
    private bool _isExitConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
    }

    /// <summary>Lets the view model stop the environment before closing. The closing is cancelled until it answers.</summary>
    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_isExitConfirmed || DataContext is not MainViewModel mainViewModel) return;
        e.Cancel = true;
        if (!await mainViewModel.PrepareExitAsync()) return;
        _isExitConfirmed = true;
        Close();
    }

    private void ProjectListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox projectListBox || projectListBox.SelectedItem is null) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (projectListBox.SelectedItem is not null) projectListBox.ScrollIntoView(projectListBox.SelectedItem);
        }, DispatcherPriority.Background);
    }
}
