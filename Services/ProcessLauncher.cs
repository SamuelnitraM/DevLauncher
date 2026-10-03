using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace DevLauncher.Services;

/// <summary>
/// Starts, stops and closes processes on behalf of the tools, logging every outcome.
/// </summary>
public sealed class ProcessLauncher
{
    /// <summary>Name of the Windows Terminal window grouping the tabs opened by DevLauncher.</summary>
    public const string WindowsTerminalWindowName = "DevLauncher";

    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromSeconds(15);

    private readonly LaunchLog _launchLog;

    public ProcessLauncher(LaunchLog launchLog)
    {
        _launchLog = launchLog;
    }

    // ════════════════════════════════════════════════════════
    //  START
    // ════════════════════════════════════════════════════════

    /// <summary>Starts an executable in the background, without any window.</summary>
    public bool StartHiddenProcess(string executablePath, string? arguments)
    {
        if (!File.Exists(executablePath))
        {
            _launchLog.Error($"❌ Introuvable : {executablePath}");
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
            _launchLog.Detail($"Processus caché : {executablePath} {arguments}");
            Process.Start(processStartInfo)?.Dispose();
            _launchLog.Info($"   ✅ {Path.GetFileName(executablePath)} lancé");
            return true;
        }
        catch (Win32Exception exception)
        {
            _launchLog.Error($"❌ {Path.GetFileName(executablePath)} : {exception.Message}");
            return false;
        }
    }

    /// <summary>Opens a file, an URL or a GUI application through the Windows shell.</summary>
    public bool StartShellProcess(string target, string? arguments = null)
    {
        try
        {
            var processStartInfo = new ProcessStartInfo(target) { UseShellExecute = true };
            if (arguments is not null) processStartInfo.Arguments = arguments;
            _launchLog.Detail($"Ouverture par le shell : {target} {arguments}");
            Process.Start(processStartInfo)?.Dispose();
            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            _launchLog.Error($"❌ {target} : {exception.Message}");
            return false;
        }
    }

    /// <summary>Starts Windows Terminal with the given command line arguments.</summary>
    public bool StartWindowsTerminal(IEnumerable<string> windowsTerminalArguments)
    {
        try
        {
            var processStartInfo = new ProcessStartInfo("wt.exe") { UseShellExecute = false };
            foreach (var argument in windowsTerminalArguments) processStartInfo.ArgumentList.Add(argument);
            _launchLog.Detail($"Windows Terminal : wt.exe {string.Join(' ', processStartInfo.ArgumentList)}");
            Process.Start(processStartInfo)?.Dispose();
            return true;
        }
        catch (Win32Exception exception)
        {
            _launchLog.Error($"❌ Windows Terminal : {exception.Message}");
            return false;
        }
    }

    /// <summary>Opens an interactive console program in its own window, in the given folder.</summary>
    public bool StartConsole(string executable, IReadOnlyList<string> arguments, string workingDirectory)
    {
        try
        {
            var processStartInfo = new ProcessStartInfo(executable) { UseShellExecute = true, WorkingDirectory = workingDirectory };
            foreach (var argument in arguments) processStartInfo.ArgumentList.Add(argument);
            _launchLog.Detail($"Console : {executable} {string.Join(' ', arguments)} (dossier {workingDirectory})");
            Process.Start(processStartInfo)?.Dispose();
            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            _launchLog.Error($"❌ {executable} : {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Opens a folder in VSCode. A path to an executable is started directly,
    /// a command name (code) goes through cmd.exe to resolve code.cmd without a console window.
    /// </summary>
    public bool StartVSCode(string projectPath)
    {
        var vscodeExecutable = AppSettings.VSCodeExecutable;
        try
        {
            var processStartInfo = vscodeExecutable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? new ProcessStartInfo(vscodeExecutable, $"\"{projectPath}\"") { UseShellExecute = false }
                : new ProcessStartInfo("cmd.exe", $"/c \"\"{vscodeExecutable}\" \"{projectPath}\"\"") { UseShellExecute = false, CreateNoWindow = true };
            _launchLog.Detail($"VSCode : {processStartInfo.FileName} {processStartInfo.Arguments}");
            Process.Start(processStartInfo)?.Dispose();
            return true;
        }
        catch (Win32Exception exception)
        {
            _launchLog.Error($"❌ VSCode ({vscodeExecutable}) : {exception.Message}");
            return false;
        }
    }

    /// <summary>Runs a command without window and waits for its exit.</summary>
    public async Task RunHiddenCommandAsync(string executable, IEnumerable<string> arguments)
    {
        try
        {
            var processStartInfo = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in arguments) processStartInfo.ArgumentList.Add(argument);
            _launchLog.Detail($"Commande : {executable} {string.Join(' ', processStartInfo.ArgumentList)}");
            using var commandProcess = Process.Start(processStartInfo);
            if (commandProcess is null) return;
            using var exitCancellation = new CancellationTokenSource(ProcessExitTimeout);
            await commandProcess.WaitForExitAsync(exitCancellation.Token);
            _launchLog.Info(commandProcess.ExitCode == 0 ? "   ✅ Terminé" : $"   ⚠️ {executable} a retourné le code {commandProcess.ExitCode}");
        }
        catch (Exception exception) when (exception is Win32Exception or OperationCanceledException)
        {
            _launchLog.Error($"⚠️ {executable} : {exception.Message}");
        }
    }

    /// <summary>
    /// Runs a command line through cmd.exe in a folder, sends each output line to the callback and returns the exit code,
    /// or null when the command cannot start or exceeds the timeout (it is then killed).
    /// </summary>
    /// <param name="environmentVariables">Variables added to the environment of the command, never written to the log (passwords).</param>
    public async Task<int?> RunCommandLineAsync(string commandLine, string workingDirectory, TimeSpan timeout, Action<string, bool> onOutputLine,
        IReadOnlyDictionary<string, string>? environmentVariables = null)
    {
        var processStartInfo = new ProcessStartInfo("cmd.exe", $"/d /s /c \"{commandLine}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        processStartInfo.Environment["NO_COLOR"] = "1";
        processStartInfo.Environment["COMPOSER_NO_INTERACTION"] = "1";
        processStartInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var (variableName, variableValue) in environmentVariables ?? new Dictionary<string, string>())
            processStartInfo.Environment[variableName] = variableValue;
        _launchLog.Detail($"Commande : {commandLine} (dossier {workingDirectory})");
        try
        {
            using var commandProcess = new Process { StartInfo = processStartInfo };
            commandProcess.OutputDataReceived += (_, eventArgs) => { if (eventArgs.Data is not null) onOutputLine(eventArgs.Data, false); };
            commandProcess.ErrorDataReceived += (_, eventArgs) => { if (eventArgs.Data is not null) onOutputLine(eventArgs.Data, true); };
            commandProcess.Start();
            // No question can be answered : an input closed at once makes interactive commands fail instead of waiting.
            commandProcess.StandardInput.Close();
            commandProcess.BeginOutputReadLine();
            commandProcess.BeginErrorReadLine();
            using var timeoutCancellation = new CancellationTokenSource(timeout);
            try
            {
                await commandProcess.WaitForExitAsync(timeoutCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                commandProcess.Kill(entireProcessTree: true);
                _launchLog.Error($"⚠️ « {commandLine} » arrêtée après {timeout.TotalMinutes:0} min");
                return null;
            }
            // The parameterless wait flushes the redirected output before the exit code is read.
            commandProcess.WaitForExit();
            return commandProcess.ExitCode;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            _launchLog.Error($"❌ « {commandLine} » : {exception.Message}");
            return null;
        }
    }

    // ════════════════════════════════════════════════════════
    //  STOP
    // ════════════════════════════════════════════════════════

    /// <summary>Kills the matching processes (with their children) and waits for their exit.</summary>
    public async Task StopProcessesAsync(string displayName, Func<Process, bool> isProcessToStop)
    {
        var runningProcesses = Process.GetProcesses();
        var processesToStop = runningProcesses.Where(process => IsMatchingLiveProcess(process, isProcessToStop)).ToList();
        foreach (var ignoredProcess in runningProcesses.Except(processesToStop)) ignoredProcess.Dispose();
        if (processesToStop.Count == 0)
        {
            _launchLog.Info($"   ℹ️ {displayName} n'était pas en cours");
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
                _launchLog.Error($"⚠️ Arrêt de {displayName} (PID {processToStop.Id}) : {exception.Message}");
            }
            finally
            {
                processToStop.Dispose();
            }
        }
        _launchLog.Info($"   ✅ {displayName} arrêté");
    }

    /// <summary>Kills a process with its children and waits for its exit. Returns false when it cannot be stopped (system process, access denied).</summary>
    public async Task<bool> StopProcessByIdAsync(int processId, string processName)
    {
        _launchLog.Info($"⏹ Arrêt de {processName} (PID {processId})…");
        try
        {
            using var processToStop = Process.GetProcessById(processId);
            processToStop.Kill(entireProcessTree: true);
            using var exitCancellation = new CancellationTokenSource(ProcessExitTimeout);
            await processToStop.WaitForExitAsync(exitCancellation.Token);
            _launchLog.Info($"   ✅ {processName} arrêté");
            return true;
        }
        catch (ArgumentException)
        {
            _launchLog.Info($"   ℹ️ {processName} n'est plus en cours");
            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or OperationCanceledException or NotSupportedException)
        {
            _launchLog.Error($"❌ Impossible d'arrêter {processName} (PID {processId}) : {exception.Message}");
            return false;
        }
    }

    /// <summary>Requests the closing of the windows of an application showing one of the given title segments.</summary>
    public void CloseApplicationWindows(string applicationName, string processName, IReadOnlyCollection<string> titleSegments)
    {
        _launchLog.Info($"⏹ Fermeture de {applicationName}…");
        var closeRequestedWindowCount = NativeWindowService.CloseWindows(processName, titleSegments);
        _launchLog.Info(closeRequestedWindowCount > 0
            ? $"   ✅ {closeRequestedWindowCount} fenêtre(s) {applicationName} fermée(s)"
            : $"   ℹ️ Aucune fenêtre {applicationName} ouverte sur {string.Join(" / ", titleSegments)}");
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
}
