using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DevLauncher.Models;

namespace DevLauncher.Services;

/// <summary>
/// Starts and stops the tools and services of a project development environment.
/// Only what was started by this service is stopped by StopAllAsync.
/// </summary>
public class LaunchService
{
    private const string WindowsTerminalWindowName = "DevLauncher";
    private const string VSCodeTasksBackupSuffix = ".devlauncher-backup";
    private static readonly TimeSpan ServerReadinessTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan ServerReadinessProbeInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan PortConnectionTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan VSCodeTasksRestoreTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromSeconds(15);

    /// <summary>XAMPP component that can be started and stopped by the launcher.</summary>
    private sealed record XamppComponent(string DisplayName, string Icon, string ProcessName, Func<ProjectProfile, bool> IsRequestedBy, Func<string> GetExecutablePath, Func<string?> GetArguments, bool RunsHidden);

    /// <summary>Long-running service command, hosted either in a Windows Terminal tab or in a VSCode task.</summary>
    private sealed record ServiceCommand(string Title, string WorkingDirectory, IReadOnlyList<string> PowerShellArguments, string ServiceProcessName);

    /// <summary>Generated VSCode tasks.json waiting to be replaced by the original file of the project.</summary>
    private sealed record PendingVSCodeTasks(string VSCodeDirectory, string TasksFilePath, string BackupFilePath, bool RemoveDirectoryWhenEmpty);

    private static readonly XamppComponent[] _xamppComponents =
    {
        new("Apache", "🌐", "httpd", profile => profile.StartApache, () => AppSettings.ApacheExe, () => null, true),
        new("MySQL", "🗃️", "mysqld", profile => profile.StartMySQL, () => AppSettings.MySQLExe, () => $"--defaults-file=\"{AppSettings.MySQLConfig}\"", true),
        new("FileZilla FTP Server", "📂", "FileZillaServer", profile => profile.StartFileZilla, () => AppSettings.FileZillaExe, () => "-compat -start", true),
        new("Panneau XAMPP", "🖥️", "xampp-control", profile => profile.ShowXamppPanel, () => AppSettings.XamppPanel, () => null, false),
    };

    private static readonly JsonSerializerOptions _vscodeTasksJsonOptions = new() { WriteIndented = true };

    private readonly ProcessEventWatcher _processEventWatcher;
    private readonly Dictionary<string, ProjectProfile> _launchedProjects = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<XamppComponent> _startedXamppComponents = new();
    private readonly Dictionary<string, PendingVSCodeTasks> _pendingVSCodeTasksByProject = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _pendingVSCodeTasksLock = new();

    public event Action<string>? LogMessage;
    public event Action<string>? LogError;

    public LaunchService(ProcessEventWatcher processEventWatcher)
    {
        _processEventWatcher = processEventWatcher;
    }

    // ════════════════════════════════════════════════════════
    //  LAUNCH
    // ════════════════════════════════════════════════════════

    public async Task LaunchAsync(string projectPath, ProjectProfile profile)
    {
        var symfonyServiceCommands = profile.IsSymfony ? BuildSymfonyServiceCommands(projectPath, profile) : new List<ServiceCommand>();
        var hostSymfonyServicesInVSCode = profile.OpenVSCode && symfonyServiceCommands.Count > 0;
        // VSCode reads tasks.json when the folder opens, so the file must exist before the editor starts.
        if (hostSymfonyServicesInVSCode) PrepareVSCodeTasks(projectPath, symfonyServiceCommands);
        OpenEditors(projectPath, profile);
        StartRequestedXamppComponents(profile);
        if (profile.OpenTerminal) OpenProjectTerminal(projectPath);
        if (!hostSymfonyServicesInVSCode && symfonyServiceCommands.Count > 0) StartServicesInWindowsTerminal(symfonyServiceCommands);
        _launchedProjects[projectPath] = profile;
        if (profile.OpenBrowser) await OpenBrowserWhenServerIsReadyAsync(projectPath, profile);
    }

    private void OpenEditors(string projectPath, ProjectProfile profile)
    {
        if (profile.OpenVSCode)
        {
            Log("💻 Ouverture de VSCode…");
            StartVSCode(projectPath);
        }
        if (profile.OpenVisualStudio)
        {
            Log("🟣 Ouverture de Visual Studio…");
            var solutionFilePath = FindSolutionFile(projectPath);
            Log(solutionFilePath is not null
                ? $"   → Solution : {Path.GetFileName(solutionFilePath)}"
                : "   → Pas de solution, ouverture du dossier…");
            StartShellProcess(AppSettings.VisualStudioExecutable, $"\"{solutionFilePath ?? projectPath}\"");
        }
    }

    private void StartRequestedXamppComponents(ProjectProfile profile)
    {
        foreach (var xamppComponent in _xamppComponents.Where(component => component.IsRequestedBy(profile)))
            StartXamppComponent(xamppComponent);
    }

    private void StartXamppComponent(XamppComponent xamppComponent)
    {
        if (ProcessHelper.IsProcessRunning(xamppComponent.ProcessName))
        {
            Log($"{xamppComponent.Icon} {xamppComponent.DisplayName} déjà en cours — ignoré");
            return;
        }
        Log($"{xamppComponent.Icon} Démarrage de {xamppComponent.DisplayName}…");
        var executablePath = xamppComponent.GetExecutablePath();
        var isStarted = xamppComponent.RunsHidden
            ? StartHiddenProcess(executablePath, xamppComponent.GetArguments())
            : StartShellProcess(executablePath, xamppComponent.GetArguments());
        if (isStarted) _startedXamppComponents.Add(xamppComponent);
    }

    private void OpenProjectTerminal(string projectPath)
    {
        Log("🖥️ Ouverture d'un terminal…");
        var isStarted = StartWindowsTerminal(new[]
        {
            "-w", WindowsTerminalWindowName, "new-tab", "--title", Path.GetFileName(projectPath),
            "--suppressApplicationTitle", "-d", projectPath,
        });
        if (isStarted) Log("   ✅ Terminal ouvert");
    }

    // ════════════════════════════════════════════════════════
    //  SYMFONY SERVICES
    // ════════════════════════════════════════════════════════

    private List<ServiceCommand> BuildSymfonyServiceCommands(string projectPath, ProjectProfile profile)
    {
        Log("⚡ Préparation des services Symfony…");
        var serviceCommands = new List<ServiceCommand>();
        if (profile.StartSymfonyServer)
            serviceCommands.Add(new ServiceCommand("Symfony Server", projectPath,
                new[] { "-NoExit", "-Command", $"symfony server:start --port={AppSettings.SymfonyPort}" }, "symfony"));
        if (profile.StartTailwind)
            serviceCommands.Add(new ServiceCommand("Tailwind Watch", projectPath,
                new[] { "-NoExit", "-Command", "symfony console tailwind:build --watch" }, "symfony"));
        if (profile.StartMercure)
        {
            var mercureScriptPath = string.IsNullOrEmpty(profile.MercureScript) ? null : Path.Combine(AppSettings.MercureDir, profile.MercureScript);
            if (mercureScriptPath is null || !File.Exists(mercureScriptPath))
                LogErr($"❌ Script Mercure introuvable dans {AppSettings.MercureDir} — Mercure ne sera pas lancé.");
            else
                serviceCommands.Add(new ServiceCommand("Mercure", AppSettings.MercureDir,
                    new[] { "-NoExit", "-File", mercureScriptPath }, "mercure"));
        }
        return serviceCommands;
    }

    private void StartServicesInWindowsTerminal(IReadOnlyList<ServiceCommand> serviceCommands)
    {
        var windowsTerminalArguments = new List<string> { "-w", WindowsTerminalWindowName };
        foreach (var serviceCommand in serviceCommands)
        {
            if (windowsTerminalArguments.Count > 2) windowsTerminalArguments.Add(";");
            windowsTerminalArguments.AddRange(new[]
            {
                "new-tab", "--title", serviceCommand.Title, "--suppressApplicationTitle",
                "-d", serviceCommand.WorkingDirectory, "powershell",
            });
            windowsTerminalArguments.AddRange(serviceCommand.PowerShellArguments);
            Log($"   → onglet {serviceCommand.Title}");
        }
        StartWindowsTerminal(windowsTerminalArguments);
    }

    /// <summary>
    /// Writes a temporary .vscode/tasks.json running the services when the folder opens.
    /// An existing tasks.json is backed up and restored as soon as one of the services has started.
    /// </summary>
    private void PrepareVSCodeTasks(string projectPath, IReadOnlyList<ServiceCommand> serviceCommands)
    {
        var vscodeDirectory = Path.Combine(projectPath, ".vscode");
        var tasksFilePath = Path.Combine(vscodeDirectory, "tasks.json");
        var backupFilePath = tasksFilePath + VSCodeTasksBackupSuffix;
        try
        {
            var removeDirectoryWhenEmpty = !Directory.Exists(vscodeDirectory);
            Directory.CreateDirectory(vscodeDirectory);
            // An existing backup means a previous generated file was never restored : the backup is the original.
            if (File.Exists(tasksFilePath) && !File.Exists(backupFilePath))
            {
                File.Move(tasksFilePath, backupFilePath);
                Log("   → tasks.json existant mis de côté");
            }
            File.WriteAllText(tasksFilePath, BuildVSCodeTasksJson(serviceCommands));
            Log("   → .vscode/tasks.json temporaire généré");
            Log("   → Les terminaux s'ouvriront dans VSCode (accepte « Autoriser les tâches automatiques » si demandé)");
            var pendingVSCodeTasks = new PendingVSCodeTasks(vscodeDirectory, tasksFilePath, backupFilePath, removeDirectoryWhenEmpty);
            lock (_pendingVSCodeTasksLock) _pendingVSCodeTasksByProject[projectPath] = pendingVSCodeTasks;
            _ = RestoreVSCodeTasksOnceServiceStartedAsync(projectPath, serviceCommands.Select(command => command.ServiceProcessName).ToHashSet(StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogErr($"❌ Impossible de générer .vscode/tasks.json : {exception.Message}");
        }
    }

    private static string BuildVSCodeTasksJson(IReadOnlyList<ServiceCommand> serviceCommands)
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
        return JsonSerializer.Serialize(new Dictionary<string, object> { ["version"] = "2.0.0", ["tasks"] = vscodeTasks }, _vscodeTasksJsonOptions);
    }

    /// <summary>Waits for a service process started by the VSCode tasks, then restores the original tasks.json.</summary>
    private async Task RestoreVSCodeTasksOnceServiceStartedAsync(string projectPath, IReadOnlySet<string> serviceProcessNames)
    {
        var serviceStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnProcessStarted(int processId, string processName)
        {
            if (serviceProcessNames.Contains(processName)) serviceStarted.TrySetResult();
        }
        _processEventWatcher.ProcessStarted += OnProcessStarted;
        try
        {
            await Task.WhenAny(serviceStarted.Task, Task.Delay(VSCodeTasksRestoreTimeout));
        }
        finally
        {
            _processEventWatcher.ProcessStarted -= OnProcessStarted;
        }
        RestoreVSCodeTasks(projectPath);
    }

    /// <summary>Removes the generated tasks.json and puts the original one back. Does nothing when already restored.</summary>
    private void RestoreVSCodeTasks(string projectPath)
    {
        PendingVSCodeTasks? pendingVSCodeTasks;
        lock (_pendingVSCodeTasksLock)
        {
            if (!_pendingVSCodeTasksByProject.Remove(projectPath, out pendingVSCodeTasks)) return;
        }
        try
        {
            if (File.Exists(pendingVSCodeTasks.TasksFilePath)) File.Delete(pendingVSCodeTasks.TasksFilePath);
            if (File.Exists(pendingVSCodeTasks.BackupFilePath))
            {
                File.Move(pendingVSCodeTasks.BackupFilePath, pendingVSCodeTasks.TasksFilePath);
                Log("♻️ tasks.json d'origine restauré");
            }
            else
            {
                Log("🗑️ tasks.json temporaire supprimé");
            }
            if (pendingVSCodeTasks.RemoveDirectoryWhenEmpty
                && Directory.Exists(pendingVSCodeTasks.VSCodeDirectory)
                && !Directory.EnumerateFileSystemEntries(pendingVSCodeTasks.VSCodeDirectory).Any())
            {
                Directory.Delete(pendingVSCodeTasks.VSCodeDirectory);
                Log("🗑️ Dossier .vscode supprimé");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogErr($"⚠️ Restauration de tasks.json : {exception.Message}");
        }
    }

    /// <summary>Restores immediately every tasks.json still waiting for its services.</summary>
    public void RestorePendingVSCodeTasks()
    {
        List<string> pendingProjectPaths;
        lock (_pendingVSCodeTasksLock) pendingProjectPaths = _pendingVSCodeTasksByProject.Keys.ToList();
        foreach (var projectPath in pendingProjectPaths) RestoreVSCodeTasks(projectPath);
    }

    // ════════════════════════════════════════════════════════
    //  BROWSER
    // ════════════════════════════════════════════════════════

    private async Task OpenBrowserWhenServerIsReadyAsync(string projectPath, ProjectProfile profile)
    {
        var serverName = profile.IsSymfony ? "Symfony Server" : "Apache";
        var serverPort = profile.IsSymfony ? AppSettings.SymfonyPort : AppSettings.LocalWebPort;
        var isServerStartedByThisLaunch = profile.IsSymfony ? profile.StartSymfonyServer : profile.StartApache;
        var projectUrl = profile.IsSymfony
            ? $"http://127.0.0.1:{serverPort}"
            : $"http://localhost{(serverPort == 80 ? string.Empty : $":{serverPort}")}/{Uri.EscapeDataString(Path.GetFileName(projectPath))}/";
        if (!await IsPortOpenAsync(serverPort))
        {
            if (!isServerStartedByThisLaunch)
            {
                LogErr($"❌ {serverName} n'est ni actif ni sélectionné : la page {projectUrl} ne peut pas s'ouvrir.");
                return;
            }
            Log($"⏳ Attente de {serverName} sur le port {serverPort}…");
            if (!await WaitForPortAsync(serverPort, ServerReadinessTimeout))
            {
                LogErr($"❌ {serverName} ne répond pas après {ServerReadinessTimeout.TotalSeconds:0}s : la page {projectUrl} ne peut pas s'ouvrir.");
                return;
            }
            Log($"✅ {serverName} est prêt");
        }
        Log($"🌍 Ouverture du navigateur : {projectUrl}");
        OpenInBrowsers(profile, projectUrl);
    }

    private void OpenInBrowsers(ProjectProfile profile, string url)
    {
        if (!profile.BrowserDefault && !profile.BrowserChrome && !profile.BrowserFirefox)
        {
            LogErr("❌ Aucun navigateur coché");
            return;
        }
        if (profile.BrowserDefault) StartShellProcess(url);
        if (profile.BrowserChrome) StartBrowser("Chrome", AppSettings.ChromeExe, url);
        if (profile.BrowserFirefox) StartBrowser("Firefox", AppSettings.FirefoxExe, url);
    }

    private void StartBrowser(string browserName, string browserExecutablePath, string url)
    {
        if (File.Exists(browserExecutablePath)) StartShellProcess(browserExecutablePath, url);
        else LogErr($"❌ {browserName} introuvable : {browserExecutablePath}");
    }

    /// <summary>Readiness probe : retries a local TCP connection until the port accepts it or the timeout expires.</summary>
    private static async Task<bool> WaitForPortAsync(int port, TimeSpan timeout)
    {
        var readinessStopwatch = Stopwatch.StartNew();
        while (readinessStopwatch.Elapsed < timeout)
        {
            if (await IsPortOpenAsync(port)) return true;
            await Task.Delay(ServerReadinessProbeInterval);
        }
        return false;
    }

    private static async Task<bool> IsPortOpenAsync(int port)
    {
        using var tcpClient = new TcpClient();
        using var connectionCancellation = new CancellationTokenSource(PortConnectionTimeout);
        try
        {
            await tcpClient.ConnectAsync(IPAddress.Loopback, port, connectionCancellation.Token);
            return true;
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException)
        {
            return false;
        }
    }

    // ════════════════════════════════════════════════════════
    //  STOP
    // ════════════════════════════════════════════════════════

    public async Task StopAllAsync()
    {
        RestorePendingVSCodeTasks();
        foreach (var (projectPath, profile) in _launchedProjects.ToList())
            await StopProjectAsync(projectPath, profile);
        _launchedProjects.Clear();
        foreach (var xamppComponent in _xamppComponents.Where(_startedXamppComponents.Contains))
        {
            Log($"⏹ Arrêt de {xamppComponent.DisplayName}…");
            await StopProcessesAsync(xamppComponent.DisplayName, process => process.ProcessName.Equals(xamppComponent.ProcessName, StringComparison.OrdinalIgnoreCase));
        }
        _startedXamppComponents.Clear();
        Log("✅ Tout est arrêté !");
    }

    private async Task StopProjectAsync(string projectPath, ProjectProfile profile)
    {
        var projectName = Path.GetFileName(projectPath);
        if (profile.IsSymfony && profile.StartSymfonyServer)
        {
            Log($"⏹ Arrêt du serveur Symfony ({projectName})…");
            await RunHiddenCommandAsync("symfony", new[] { "server:stop", "--dir", projectPath });
        }
        if (profile.IsSymfony && profile.StartTailwind)
        {
            Log($"⏹ Arrêt de Tailwind ({projectName})…");
            await StopProcessesAsync("Tailwind", process =>
                process.ProcessName.StartsWith("tailwindcss", StringComparison.OrdinalIgnoreCase) && ProcessHelper.IsExecutableInside(process, projectPath));
        }
        if (profile.IsSymfony && profile.StartMercure)
        {
            Log("⏹ Arrêt de Mercure…");
            await StopProcessesAsync("Mercure", process => process.ProcessName.Equals("mercure", StringComparison.OrdinalIgnoreCase));
        }
        if (profile.OpenVSCode) CloseEditorWindows("VSCode", "Code", new[] { projectName });
        if (profile.OpenVisualStudio)
        {
            var solutionFilePath = FindSolutionFile(projectPath);
            var titleSegments = solutionFilePath is null ? new[] { projectName } : new[] { projectName, Path.GetFileNameWithoutExtension(solutionFilePath) };
            CloseEditorWindows("Visual Studio", "devenv", titleSegments);
        }
    }

    private void CloseEditorWindows(string editorName, string editorProcessName, IReadOnlyCollection<string> titleSegments)
    {
        Log($"⏹ Fermeture de {editorName}…");
        var closeRequestedWindowCount = NativeWindowService.CloseWindows(editorProcessName, titleSegments);
        Log(closeRequestedWindowCount > 0
            ? $"   ✅ {closeRequestedWindowCount} fenêtre(s) {editorName} fermée(s)"
            : $"   ℹ️ Aucune fenêtre {editorName} ouverte sur {string.Join(" / ", titleSegments)}");
    }

    /// <summary>Kills the matching processes (with their children) and waits for their exit.</summary>
    private async Task StopProcessesAsync(string displayName, Func<Process, bool> isProcessToStop)
    {
        var runningProcesses = Process.GetProcesses();
        var processesToStop = runningProcesses.Where(process => IsMatchingLiveProcess(process, isProcessToStop)).ToList();
        foreach (var ignoredProcess in runningProcesses.Except(processesToStop)) ignoredProcess.Dispose();
        if (processesToStop.Count == 0)
        {
            Log($"   ℹ️ {displayName} n'était pas en cours");
            return;
        }
        using var exitCancellation = new CancellationTokenSource(ProcessExitTimeout);
        foreach (var processToStop in processesToStop)
        {
            try
            {
                processToStop.Kill(entireProcessTree: true);
                await processToStop.WaitForExitAsync(exitCancellation.Token);
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or OperationCanceledException)
            {
                LogErr($"⚠️ Arrêt de {displayName} (PID {processToStop.Id}) : {exception.Message}");
            }
            finally
            {
                processToStop.Dispose();
            }
        }
        Log($"   ✅ {displayName} arrêté");
    }

    /// <summary>Evaluates a process filter, a process exiting during the evaluation being considered as not matching.</summary>
    private static bool IsMatchingLiveProcess(Process process, Func<Process, bool> isProcessToStop)
    {
        try
        {
            return isProcessToStop(process);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    // ════════════════════════════════════════════════════════
    //  PROCESS HELPERS
    // ════════════════════════════════════════════════════════

    /// <summary>Starts an executable in the background, without any window.</summary>
    private bool StartHiddenProcess(string executablePath, string? arguments)
    {
        if (!File.Exists(executablePath))
        {
            LogErr($"❌ Introuvable : {executablePath}");
            return false;
        }
        try
        {
            var processStartInfo = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty,
            };
            if (arguments is not null) processStartInfo.Arguments = arguments;
            Process.Start(processStartInfo)?.Dispose();
            Log($"   ✅ {Path.GetFileName(executablePath)} lancé");
            return true;
        }
        catch (Win32Exception exception)
        {
            LogErr($"❌ {Path.GetFileName(executablePath)} : {exception.Message}");
            return false;
        }
    }

    /// <summary>Opens a file, an URL or a GUI application through the Windows shell.</summary>
    private bool StartShellProcess(string target, string? arguments = null)
    {
        try
        {
            var processStartInfo = new ProcessStartInfo(target) { UseShellExecute = true };
            if (arguments is not null) processStartInfo.Arguments = arguments;
            Process.Start(processStartInfo)?.Dispose();
            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            LogErr($"❌ {target} : {exception.Message}");
            return false;
        }
    }

    /// <summary>Starts Windows Terminal with the given command line arguments.</summary>
    private bool StartWindowsTerminal(IEnumerable<string> windowsTerminalArguments)
    {
        try
        {
            var processStartInfo = new ProcessStartInfo("wt.exe") { UseShellExecute = false };
            foreach (var argument in windowsTerminalArguments) processStartInfo.ArgumentList.Add(argument);
            Process.Start(processStartInfo)?.Dispose();
            return true;
        }
        catch (Win32Exception exception)
        {
            LogErr($"❌ Windows Terminal : {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Opens a folder in VSCode. A path to an executable is started directly,
    /// a command name (code) goes through cmd.exe to resolve code.cmd without a console window.
    /// </summary>
    private void StartVSCode(string projectPath)
    {
        var vscodeExecutable = AppSettings.VSCodeExecutable;
        try
        {
            var processStartInfo = vscodeExecutable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? new ProcessStartInfo(vscodeExecutable, $"\"{projectPath}\"") { UseShellExecute = false }
                : new ProcessStartInfo("cmd.exe", $"/c \"\"{vscodeExecutable}\" \"{projectPath}\"\"") { UseShellExecute = false, CreateNoWindow = true };
            Process.Start(processStartInfo)?.Dispose();
        }
        catch (Win32Exception exception)
        {
            LogErr($"❌ VSCode ({vscodeExecutable}) : {exception.Message}");
        }
    }

    /// <summary>Runs a command without window and waits for its exit.</summary>
    private async Task RunHiddenCommandAsync(string executable, IEnumerable<string> arguments)
    {
        try
        {
            var processStartInfo = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in arguments) processStartInfo.ArgumentList.Add(argument);
            using var commandProcess = Process.Start(processStartInfo);
            if (commandProcess is null) return;
            using var exitCancellation = new CancellationTokenSource(ProcessExitTimeout);
            await commandProcess.WaitForExitAsync(exitCancellation.Token);
            Log(commandProcess.ExitCode == 0 ? "   ✅ Terminé" : $"   ⚠️ {executable} a retourné le code {commandProcess.ExitCode}");
        }
        catch (Exception exception) when (exception is Win32Exception or OperationCanceledException)
        {
            LogErr($"⚠️ {executable} : {exception.Message}");
        }
    }

    /// <summary>Returns the first solution file (.sln or .slnx) at the root of the project, if any.</summary>
    private static string? FindSolutionFile(string projectPath)
    {
        try
        {
            return Directory.EnumerateFiles(projectPath, "*.sln")
                .Concat(Directory.EnumerateFiles(projectPath, "*.slnx"))
                .FirstOrDefault();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Log(string message) => LogMessage?.Invoke(message);
    private void LogErr(string message) => LogError?.Invoke(message);
}
