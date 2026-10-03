namespace DevLauncher.ViewModels;

/// <summary>Outcome of the settings window.</summary>
public enum SettingsEditResult
{
    Cancelled,
    Saved,
    /// <summary>Settings and profiles were replaced by an import.</summary>
    Imported,
}

/// <summary>Dialogs and system interactions requested by the view models, implemented by the views.</summary>
public interface IUserInteractionService
{
    bool Confirm(string message, string title);

    void ShowWarning(string message, string title);

    /// <summary>Returns true for yes, false for no, null when cancelled.</summary>
    bool? AskYesNoCancel(string message, string title);

    /// <summary>Asks for a profile name. Returns null when cancelled.</summary>
    string? AskProfileName(string dialogTitle, string confirmLabel, string initialProfileName);

    /// <summary>Asks for a folder. Returns null when cancelled.</summary>
    string? PickFolder(string dialogTitle);

    /// <summary>Opens the settings window and tells what changed.</summary>
    SettingsEditResult EditSettings();

    void CopyToClipboard(string text);
}
