using System.Windows;
using System.Windows.Shell;
using DevLauncher.Models;
using DevLauncher.Services;
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

    public SettingsEditResult EditSettings()
    {
        var settingsWindow = new SettingsWindow { Owner = _ownerWindow };
        if (settingsWindow.ShowDialog() != true) return SettingsEditResult.Cancelled;
        return settingsWindow.IsImported ? SettingsEditResult.Imported : SettingsEditResult.Saved;
    }

    public void CopyToClipboard(string text)
    {
        if (!string.IsNullOrEmpty(text)) Clipboard.SetText(text);
    }

    public PortConflictDecision AskPortConflict(PortConflict portConflict)
    {
        var conflictMessage = $"Le port {portConflict.Port} dont {portConflict.ToolName} a besoin est occupé par {portConflict.OwnerProcessName} (PID {portConflict.OwnerProcessId}).\n\n"
            + $"Oui : arrêter {portConflict.OwnerProcessName} puis lancer\n"
            + "Non : lancer quand même\n"
            + "Annuler : ne pas lancer";
        return MessageBox.Show(_ownerWindow, conflictMessage, "🔌 Port occupé", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning) switch
        {
            MessageBoxResult.Yes => PortConflictDecision.StopOwner,
            MessageBoxResult.No => PortConflictDecision.Ignore,
            _ => PortConflictDecision.CancelLaunch,
        };
    }

    /// <summary>Each entry of the jump list starts DevLauncher on the project, which forwards it to the running instance.</summary>
    public void UpdateJumpList(IReadOnlyList<ProjectListEntry> favoriteProjects, IReadOnlyList<ProjectListEntry> recentProjects)
    {
        var executablePath = Environment.ProcessPath;
        if (executablePath is null || Application.Current is null) return;
        JumpTask CreateProjectTask(ProjectListEntry projectEntry, string category) => new()
        {
            Title = projectEntry.Name,
            Description = $"Lancer {projectEntry.Path}",
            ApplicationPath = executablePath,
            Arguments = $"--project \"{projectEntry.Path}\"",
            IconResourcePath = executablePath,
            CustomCategory = category,
        };
        var jumpList = new JumpList { ShowRecentCategory = false, ShowFrequentCategory = false };
        foreach (var favoriteProject in favoriteProjects) jumpList.JumpItems.Add(CreateProjectTask(favoriteProject, "★ Favoris"));
        foreach (var recentProject in recentProjects.Where(recentProject => !favoriteProjects.Any(favoriteProject => favoriteProject.Path == recentProject.Path)))
            jumpList.JumpItems.Add(CreateProjectTask(recentProject, "🕘 Récemment lancés"));
        JumpList.SetJumpList(Application.Current, jumpList);
        jumpList.Apply();
    }

    public void ShowStatistics(IReadOnlyList<ProjectLaunchStatistics> projectStatistics)
        => new StatisticsWindow(projectStatistics) { Owner = _ownerWindow }.ShowDialog();
}
