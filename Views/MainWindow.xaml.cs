using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DevLauncher.Views;

/// <summary>Main window. Holds only view behavior : initial size and keeping the selected project visible.</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
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
