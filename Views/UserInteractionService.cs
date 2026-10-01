using System.Windows;
using DevLauncher.ViewModels;

namespace DevLauncher.Views;

/// <summary>Dialogs and clipboard access requested by the view models, owned by the main window.</summary>
public sealed class UserInteractionService : IUserInteractionService
{
    private readonly Window _ownerWindow;

    public UserInteractionService(Window ownerWindow)
    {
        _ownerWindow = ownerWindow;
    }

    public bool Confirm(string message, string title)
        => MessageBox.Show(_ownerWindow, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowWarning(string message, string title)
        => MessageBox.Show(_ownerWindow, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public bool? AskYesNoCancel(string message, string title)
        => MessageBox.Show(_ownerWindow, message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
        {
            MessageBoxResult.Yes => true,
            MessageBoxResult.No => false,
            _ => null,
        };

    public string? AskProfileName(string dialogTitle, string confirmLabel, string initialProfileName)
    {
        var profileNameDialog = new ProfileNameDialog(dialogTitle, confirmLabel, initialProfileName) { Owner = _ownerWindow };
        return profileNameDialog.ShowDialog() == true ? profileNameDialog.ProfileName : null;
    }

    public string? PickFolder(string dialogTitle)
    {
        var folderDialog = new Microsoft.Win32.OpenFolderDialog { Title = dialogTitle };
        return folderDialog.ShowDialog(_ownerWindow) == true ? folderDialog.FolderName : null;
    }

    public bool EditSettings() => new SettingsWindow { Owner = _ownerWindow }.ShowDialog() == true;

    public void CopyToClipboard(string text)
    {
        if (!string.IsNullOrEmpty(text)) Clipboard.SetText(text);
    }
}
