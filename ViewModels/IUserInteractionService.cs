using DevLauncher.Services;

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

    /// <summary>Tells which program holds a port needed by the launch and asks what to do.</summary>
    PortConflictDecision AskPortConflict(PortConflict portConflict);

    /// <summary>Replaces the favorite and recent projects of the jump list of the taskbar icon.</summary>
    void UpdateJumpList(IReadOnlyList<Models.ProjectListEntry> favoriteProjects, IReadOnlyList<Models.ProjectListEntry> recentProjects);

    /// <summary>Shows a Windows notification when they are enabled, optionally only while DevLauncher is not the active window.</summary>
    void ShowNotification(string title, string message, bool isWarning, bool onlyWhenInBackground);

    /// <summary>Shows the command palette and returns the chosen command, null when dismissed.</summary>
    PaletteCommand? ShowCommandPalette(CommandPaletteViewModel paletteViewModel);

    /// <summary>Shows the launch figures of the projects.</summary>
    void ShowStatistics(IReadOnlyList<ProjectLaunchStatistics> projectStatistics);
}
