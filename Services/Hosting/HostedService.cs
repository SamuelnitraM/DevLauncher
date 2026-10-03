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
public sealed class HostedService
{
    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromSeconds(15);

    private readonly object _processLock = new();
    private Process? _serviceProcess;
    private TaskCompletionSource? _processExitCompletion;
    private bool _isStopRequested;

    private readonly KillOnCloseJob? _killOnCloseJob;
    private readonly Regex? _readinessRegex;
    private TaskCompletionSource _readinessCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <param name="killOnCloseJob">Job killing the process when DevLauncher exits, null when unavailable.</param>
    public HostedService(string projectPath, ServiceCommand command, KillOnCloseJob? killOnCloseJob)
    {
        ProjectPath = projectPath;
        Command = command;
        _killOnCloseJob = killOnCloseJob;
        _readinessRegex = command.ReadinessPattern is null ? null : new Regex(command.ReadinessPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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

    /// <summary>True when the service declares a readiness line.</summary>
    public bool HasReadinessPattern => _readinessRegex is not null;

    /// <summary>True once the readiness line has been printed by the current run.</summary>
    public bool IsReady
    {
        get
        {
            lock (_processLock) return _readinessCompletion.Task.IsCompleted;
        }
    }

    /// <summary>Raised on any thread when the readiness line is printed.</summary>
    public event Action? BecameReady;

    /// <summary>Returns true once the service is ready, false when the timeout expires or the service has no readiness line.</summary>
    public async Task<bool> WaitUntilReadyAsync(TimeSpan timeout)
    {
        if (_readinessRegex is null) return false;
        Task readinessTask;
        lock (_processLock) readinessTask = _readinessCompletion.Task;
        try
        {
            await readinessTask.WaitAsync(timeout);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>Raised on any thread for each output line. Parameters : line, does it look like an error ?</summary>
    public event Action<string, bool>? OutputReceived;

    /// <summary>Raised on the starting thread when the process has started.</summary>
    public event Action? Started;

    /// <summary>Raised on any thread when the process has exited. Parameters : exit code, was the stop requested ?</summary>
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
                // A hidden console rather than no console : PHP and other console programs need valid console handles.
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Hidden,
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
            foreach (var (variableName, variableValue) in Command.EnvironmentVariables ?? new Dictionary<string, string>())
                processStartInfo.Environment[variableName] = variableValue;
            var serviceProcess = new Process { StartInfo = processStartInfo, EnableRaisingEvents = true };
            var processExitCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            serviceProcess.OutputDataReceived += (_, eventArgs) => PublishOutputLine(eventArgs.Data);
            serviceProcess.ErrorDataReceived += (_, eventArgs) => PublishOutputLine(eventArgs.Data);
            serviceProcess.Exited += (_, _) => OnProcessExited(serviceProcess, processExitCompletion);
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
            if (_killOnCloseJob is not null && !_killOnCloseJob.TryAssign(serviceProcess))
                OutputReceived?.Invoke("⚠️ Ce service ne pourra pas être arrêté automatiquement si DevLauncher est tué", true);
            _isStopRequested = false;
            if (_readinessCompletion.Task.IsCompleted) _readinessCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _serviceProcess = serviceProcess;
            _processExitCompletion = processExitCompletion;
            serviceProcess.BeginOutputReadLine();
            serviceProcess.BeginErrorReadLine();
        }
        Started?.Invoke();
        return true;
    }

    /// <summary>Kills the process with its children and waits for the exit notification.</summary>
    public async Task StopAsync()
    {
        Task processExitTask;
        lock (_processLock)
        {
            if (_serviceProcess is null || _processExitCompletion is null) return;
            _isStopRequested = true;
            processExitTask = _processExitCompletion.Task;
            KillProcessTree(_serviceProcess);
        }
        try
        {
            await processExitTask.WaitAsync(ProcessExitTimeout);
        }
        catch (TimeoutException)
        {
            OutputReceived?.Invoke($"⚠️ Le processus ne s'est pas terminé après {ProcessExitTimeout.TotalSeconds:0}s", true);
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
        lock (_processLock)
        {
            if (_serviceProcess is null) return;
            _isStopRequested = true;
            KillProcessTree(_serviceProcess);
        }
    }

    /// <summary>Must be called under the process lock, before the exit handler disposes the process.</summary>
    private static void KillProcessTree(Process serviceProcess)
    {
        try
        {
            serviceProcess.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            // The process is already exiting : the exit handler completes the stop.
        }
    }

    /// <summary>Releases the process only here, once no other method can use it anymore.</summary>
    private void OnProcessExited(Process serviceProcess, TaskCompletionSource processExitCompletion)
    {
        bool isStopRequested;
        int exitCode;
        lock (_processLock)
        {
            if (_serviceProcess != serviceProcess) return;
            _serviceProcess = null;
            _processExitCompletion = null;
            isStopRequested = _isStopRequested;
            exitCode = serviceProcess.ExitCode;
            serviceProcess.Dispose();
        }
        processExitCompletion.TrySetResult();
        Exited?.Invoke(exitCode, isStopRequested);
    }

    private void PublishOutputLine(string? outputLine)
    {
        if (outputLine is null) return;
        var cleanLine = OutputLineClassifier.RemoveAnsiSequences(outputLine);
        OutputReceived?.Invoke(cleanLine, OutputLineClassifier.LooksLikeError(cleanLine));
        if (_readinessRegex is null || !_readinessRegex.IsMatch(cleanLine)) return;
        bool isNowReady;
        lock (_processLock) isNowReady = _readinessCompletion.TrySetResult();
        if (isNowReady) BecameReady?.Invoke();
    }
}
