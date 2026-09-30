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
    private bool _isExitQuestionPending;

    public MainWindow()
    {
        InitializeComponent();
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
    }

    /// <summary>
    /// With a running environment, the closing is cancelled while the view model asks what to do,
    /// then requested again once the current closing is over.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_isExitConfirmed || DataContext is not MainViewModel mainViewModel || !mainViewModel.HasActiveEnvironment) return;
        e.Cancel = true;
        if (_isExitQuestionPending) return;
        _isExitQuestionPending = true;
        _ = ConfirmExitAsync(mainViewModel);
    }

    private async Task ConfirmExitAsync(MainViewModel mainViewModel)
    {
        var shouldExit = await mainViewModel.PrepareExitAsync();
        _isExitQuestionPending = false;
        if (!shouldExit) return;
        _isExitConfirmed = true;
        // Close cannot be called from inside a closing : it is queued after it.
        await Dispatcher.BeginInvoke(Close, DispatcherPriority.Normal);
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
