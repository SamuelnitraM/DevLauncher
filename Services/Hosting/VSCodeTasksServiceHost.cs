using System.IO;
using System.Text.Json;

namespace DevLauncher.Services.Hosting;

/// <summary>
/// Hosts the services as VSCode tasks running when the folder opens, through a temporary .vscode/tasks.json.
/// An existing tasks.json is backed up and restored as soon as one of the services has started.
/// </summary>
public sealed class VSCodeTasksServiceHost
{
    private const string TasksBackupSuffix = ".devlauncher-backup";
    private static readonly TimeSpan TasksRestoreTimeout = TimeSpan.FromMinutes(2);
    private static readonly JsonSerializerOptions _tasksJsonOptions = new() { WriteIndented = true };

    /// <summary>Generated tasks.json waiting to be replaced by the original file of the project.</summary>
    private sealed record PendingTasksFile(string VSCodeDirectory, string TasksFilePath, string BackupFilePath, bool RemoveDirectoryWhenEmpty);

    private readonly ProcessEventWatcher _processEventWatcher;
    private readonly LaunchLog _launchLog;
    private readonly Dictionary<string, PendingTasksFile> _pendingTasksFilesByProject = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _pendingTasksFilesLock = new();

    public VSCodeTasksServiceHost(ProcessEventWatcher processEventWatcher, LaunchLog launchLog)
    {
        _processEventWatcher = processEventWatcher;
        _launchLog = launchLog;
    }

    /// <summary>Writes the tasks file. Must run before VSCode opens the folder, since VSCode reads it at that moment.</summary>
    public void PrepareServices(string projectPath, IReadOnlyList<ServiceCommand> serviceCommands)
    {
        var vscodeDirectory = Path.Combine(projectPath, ".vscode");
        var tasksFilePath = Path.Combine(vscodeDirectory, "tasks.json");
        var backupFilePath = tasksFilePath + TasksBackupSuffix;
        try
        {
            var removeDirectoryWhenEmpty = !Directory.Exists(vscodeDirectory);
            Directory.CreateDirectory(vscodeDirectory);
            // An existing backup means a previous generated file was never restored : the backup is the original.
            if (File.Exists(tasksFilePath) && !File.Exists(backupFilePath))
            {
                File.Move(tasksFilePath, backupFilePath);
                _launchLog.Info("   → tasks.json existant mis de côté");
            }
            File.WriteAllText(tasksFilePath, BuildTasksJson(serviceCommands));
            foreach (var serviceCommand in serviceCommands) _launchLog.Info($"   → tâche VSCode {serviceCommand.Title}");
            _launchLog.Info("   → Accepte « Autoriser les tâches automatiques » dans VSCode si demandé");
            lock (_pendingTasksFilesLock)
                _pendingTasksFilesByProject[projectPath] = new PendingTasksFile(vscodeDirectory, tasksFilePath, backupFilePath, removeDirectoryWhenEmpty);
            _ = RestoreOnceServiceStartedAsync(projectPath, serviceCommands.Select(command => command.ServiceProcessName).ToHashSet(StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _launchLog.Error($"❌ Impossible de générer .vscode/tasks.json : {exception.Message}");
        }
    }

    /// <summary>Restores immediately every tasks.json still waiting for its services.</summary>
    public void RestorePendingTasksFiles()
    {
        List<string> pendingProjectPaths;
        lock (_pendingTasksFilesLock) pendingProjectPaths = _pendingTasksFilesByProject.Keys.ToList();
        foreach (var projectPath in pendingProjectPaths) RestoreTasksFile(projectPath);
    }

    private static string BuildTasksJson(IReadOnlyList<ServiceCommand> serviceCommands)
    {
        var vscodeTasks = serviceCommands.Select(serviceCommand => new Dictionary<string, object>
        {
            ["label"] = serviceCommand.Title,
            ["type"] = "process",
            ["command"] = "powershell",
            ["args"] = serviceCommand.PowerShellArguments,
            ["options"] = new Dictionary<string, object> { ["cwd"] = serviceCommand.WorkingDirectory },
            ["isBackground"] = true,
            ["problemMatcher"] = Array.Empty<string>(),
            ["presentation"] = new Dictionary<string, object> { ["panel"] = "dedicated", ["reveal"] = "always" },
            ["runOptions"] = new Dictionary<string, object> { ["runOn"] = "folderOpen" },
        });
        return JsonSerializer.Serialize(new Dictionary<string, object> { ["version"] = "2.0.0", ["tasks"] = vscodeTasks }, _tasksJsonOptions);
    }

    /// <summary>Waits for a service process started by the VSCode tasks, then restores the original tasks.json.</summary>
    private async Task RestoreOnceServiceStartedAsync(string projectPath, IReadOnlySet<string> serviceProcessNames)
    {
        var serviceStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnProcessStarted(int processId, string processName)
        {
            if (serviceProcessNames.Contains(processName)) serviceStarted.TrySetResult();
        }
        _processEventWatcher.ProcessStarted += OnProcessStarted;
        try
        {
            await Task.WhenAny(serviceStarted.Task, Task.Delay(TasksRestoreTimeout));
        }
        finally
        {
            _processEventWatcher.ProcessStarted -= OnProcessStarted;
        }
        RestoreTasksFile(projectPath);
    }

    /// <summary>Removes the generated tasks.json and puts the original one back. Does nothing when already restored.</summary>
    private void RestoreTasksFile(string projectPath)
    {
        PendingTasksFile? pendingTasksFile;
        lock (_pendingTasksFilesLock)
        {
            if (!_pendingTasksFilesByProject.Remove(projectPath, out pendingTasksFile)) return;
        }
        try
        {
            if (File.Exists(pendingTasksFile.TasksFilePath)) File.Delete(pendingTasksFile.TasksFilePath);
            if (File.Exists(pendingTasksFile.BackupFilePath))
            {
                File.Move(pendingTasksFile.BackupFilePath, pendingTasksFile.TasksFilePath);
                _launchLog.Info("♻️ tasks.json d'origine restauré");
            }
            else
            {
                _launchLog.Info("🗑️ tasks.json temporaire supprimé");
            }
            if (pendingTasksFile.RemoveDirectoryWhenEmpty
                && Directory.Exists(pendingTasksFile.VSCodeDirectory)
                && !Directory.EnumerateFileSystemEntries(pendingTasksFile.VSCodeDirectory).Any())
            {
                Directory.Delete(pendingTasksFile.VSCodeDirectory);
                _launchLog.Info("🗑️ Dossier .vscode supprimé");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _launchLog.Error($"⚠️ Restauration de tasks.json : {exception.Message}");
        }
    }
}
