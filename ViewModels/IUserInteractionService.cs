namespace DevLauncher.ViewModels;

/// <summary>Dialogs and system interactions requested by the view models, implemented by the views.</summary>
public interface IUserInteractionService
{
    bool Confirm(string message, string title);

    void ShowWarning(string message, string title);

    /// <summary>Returns true for yes, false for no, null when cancelled.</summary>
    bool? AskYesNoCancel(string message, string title);

    /// <summary>Asks for a profile name. Returns null when cancelled.</summary>
    string? AskProfileName(string dialogTitle, string confirmLabel, string initialProfileName);

    /// <summary>Opens the settings window. Returns true when the settings were saved.</summary>
    bool EditSettings();

    void CopyToClipboard(string text);
}
