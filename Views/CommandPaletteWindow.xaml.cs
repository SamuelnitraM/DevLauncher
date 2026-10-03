using System.Windows;
using System.Windows.Input;
using DevLauncher.ViewModels;

namespace DevLauncher.Views;

/// <summary>Command palette shown at the top of the main window : the keyboard drives it, a click outside closes it.</summary>
public partial class CommandPaletteWindow : Window
{
    private readonly CommandPaletteViewModel _paletteViewModel;
    private bool _isClosing;

    public CommandPaletteWindow(CommandPaletteViewModel paletteViewModel, Window ownerWindow)
    {
        InitializeComponent();
        _paletteViewModel = paletteViewModel;
        DataContext = paletteViewModel;
        Owner = ownerWindow;
        Loaded += (_, _) =>
        {
            Left = ownerWindow.Left + (ownerWindow.ActualWidth - ActualWidth) / 2;
            Top = ownerWindow.Top + 80;
            SearchBox.Focus();
        };
        Deactivated += (_, _) => CloseWith(null);
    }

    /// <summary>Command chosen by the user, null when the palette was dismissed.</summary>
    public PaletteCommand? ChosenCommand { get; private set; }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                _paletteViewModel.MoveSelection(1);
                break;
            case Key.Up:
                _paletteViewModel.MoveSelection(-1);
                break;
            case Key.Enter:
                CloseWith(_paletteViewModel.SelectedCommand);
                break;
            case Key.Escape:
                CloseWith(null);
                break;
            default:
                return;
        }
        if (_paletteViewModel.SelectedCommand is not null) CommandsList.ScrollIntoView(_paletteViewModel.SelectedCommand);
        e.Handled = true;
    }

    private void CommandsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => CloseWith(_paletteViewModel.SelectedCommand);

    /// <summary>Closes once, whether by a choice, the Escape key or the loss of focus.</summary>
    private void CloseWith(PaletteCommand? chosenCommand)
    {
        if (_isClosing) return;
        _isClosing = true;
        ChosenCommand = chosenCommand;
        DialogResult = chosenCommand is not null;
    }
}
