using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DevLauncher.Services.Hosting;

/// <summary>
/// Service process owned by DevLauncher : its output is captured, its exit is notified,
/// and it is stopped with its whole process tree.
/// </summary>
public sealed partial class HostedService
{
    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromSeconds(15);

    private readonly object _processLock = new();
    private Process? _serviceProcess;
    private bool _isStopRequested;

    public HostedService(string projectPath, ServiceCommand command)
    {
        ProjectPath = projectPath;
        Command = command;
    }

    public string ProjectPath { get; }
    public ServiceCommand Command { get; }
    public string ProjectName => Path.GetFileName(ProjectPath);

    public bool IsRunning
    {
        get
        {
            lock (_processLock) return _serviceProcess is not null;
        }
    }

    /// <summary>Raised on a background thread for each output line. Parameters : line, does it look like an error ?</summary>
    public event Action<string, bool>? OutputReceived;

    /// <summary>Raised on a background thread when the process has started.</summary>
    public event Action? Started;

    /// <summary>Raised on a background thread when the process has exited. Parameters : exit code, was the stop requested ?</summary>
    public event Action<int, bool>? Exited;

    /// <summary>Starts the process. Returns false when it cannot start (the reason is sent as an output line).</summary>
    public bool Start()
    {
        lock (_processLock)
        {
            if (_serviceProcess is not null) return true;
            var processStartInfo = new ProcessStartInfo(Command.Executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Command.WorkingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // Watchers such as Tailwind stop when their input closes : the input stays open for the whole run.
                RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var argument in Command.Arguments) processStartInfo.ArgumentList.Add(argument);
            processStartInfo.Environment["NO_COLOR"] = "1";
            var serviceProcess = new Process { StartInfo = processStartInfo, EnableRaisingEvents = true };
            serviceProcess.OutputDataReceived += (_, eventArgs) => PublishOutputLine(eventArgs.Data);
            serviceProcess.ErrorDataReceived += (_, eventArgs) => PublishOutputLine(eventArgs.Data);
            serviceProcess.Exited += (_, _) => OnProcessExited(serviceProcess);
            try
            {
                serviceProcess.Start();
            }
            catch (Win32Exception exception)
            {
                serviceProcess.Dispose();
                OutputReceived?.Invoke($"❌ Impossible de lancer « {Command.Executable} » : {exception.Message}", true);
                return false;
            }
            _isStopRequested = false;
            _serviceProcess = serviceProcess;
            serviceProcess.BeginOutputReadLine();
            serviceProcess.BeginErrorReadLine();
        }
        Started?.Invoke();
        return true;
    }

    /// <summary>Kills the process with its children and waits for its exit.</summary>
    public async Task StopAsync()
    {
        Process? serviceProcess;
        lock (_processLock)
        {
            serviceProcess = _serviceProcess;
            if (serviceProcess is null) return;
            _isStopRequested = true;
        }
        try
        {
            serviceProcess.Kill(entireProcessTree: true);
            using var exitCancellation = new CancellationTokenSource(ProcessExitTimeout);
            await serviceProcess.WaitForExitAsync(exitCancellation.Token);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            OutputReceived?.Invoke($"⚠️ Arrêt incomplet : {exception.Message}", true);
        }
    }

    public async Task RestartAsync()
    {
        await StopAsync();
        Start();
    }

    /// <summary>Kills the process with its children without waiting. Used when the application closes.</summary>
    public void Kill()
    {
        Process? serviceProcess;
        lock (_processLock)
        {
            serviceProcess = _serviceProcess;
            if (serviceProcess is null) return;
            _isStopRequested = true;
        }
        try
        {
            serviceProcess.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            // The process is already exiting.
        }
    }

    private void OnProcessExited(Process serviceProcess)
    {
        bool isStopRequested;
        int exitCode;
        lock (_processLock)
        {
            if (_serviceProcess != serviceProcess) return;
            _serviceProcess = null;
            isStopRequested = _isStopRequested;
            exitCode = serviceProcess.ExitCode;
        }
        serviceProcess.Dispose();
        Exited?.Invoke(exitCode, isStopRequested);
    }

    private void PublishOutputLine(string? outputLine)
    {
        if (outputLine is null) return;
        var cleanLine = AnsiEscapeSequenceRegex().Replace(outputLine, string.Empty);
        OutputReceived?.Invoke(cleanLine, ErrorMarkerRegex().IsMatch(cleanLine));
    }

    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex AnsiEscapeSequenceRegex();

    [GeneratedRegex(@"\b(error|erreur|exception|fatal|critical|failed)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorMarkerRegex();
}
