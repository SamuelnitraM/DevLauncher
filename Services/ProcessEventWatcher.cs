using System.Management;
using System.Runtime.InteropServices;

namespace DevLauncher.Services;

/// <summary>
/// Raises an event as soon as a process starts or stops on the machine, through the WMI process traces.
/// The traces require administrator rights, which the application manifest requests.
/// </summary>
public sealed class ProcessEventWatcher : IDisposable
{
    private ManagementEventWatcher? _processStartWatcher;
    private ManagementEventWatcher? _processStopWatcher;

    /// <summary>Raised on a WMI thread when a process starts. Parameters : process id, process name without extension.</summary>
    public event Action<int, string>? ProcessStarted;

    /// <summary>Raised on a WMI thread when a process stops. Parameter : process id.</summary>
    public event Action<int>? ProcessStopped;

    /// <summary>True when the process traces are being received.</summary>
    public bool IsWatching { get; private set; }

    /// <summary>Subscribes to the WMI process traces. Returns false when they are not available.</summary>
    public bool Start()
    {
        try
        {
            _processStartWatcher = CreateTraceWatcher("Win32_ProcessStartTrace", OnProcessStartTraceArrived);
            _processStopWatcher = CreateTraceWatcher("Win32_ProcessStopTrace", OnProcessStopTraceArrived);
            IsWatching = true;
        }
        catch (Exception exception) when (exception is ManagementException or COMException or UnauthorizedAccessException)
        {
            StopWatchers();
        }
        return IsWatching;
    }

    private static ManagementEventWatcher CreateTraceWatcher(string traceClassName, EventArrivedEventHandler onTraceArrived)
    {
        var traceWatcher = new ManagementEventWatcher(new WqlEventQuery($"SELECT * FROM {traceClassName}"));
        traceWatcher.EventArrived += onTraceArrived;
        traceWatcher.Start();
        return traceWatcher;
    }

    private void OnProcessStartTraceArrived(object sender, EventArrivedEventArgs eventArgs)
    {
        var processId = Convert.ToInt32(eventArgs.NewEvent["ProcessID"]);
        var reportedProcessName = eventArgs.NewEvent["ProcessName"] as string ?? string.Empty;
        // The trace may truncate long image names, so the live process name is preferred.
        var processName = ProcessHelper.TryGetProcessName(processId) ?? ProcessHelper.NormalizeProcessName(reportedProcessName);
        ProcessStarted?.Invoke(processId, processName);
    }

    private void OnProcessStopTraceArrived(object sender, EventArrivedEventArgs eventArgs)
        => ProcessStopped?.Invoke(Convert.ToInt32(eventArgs.NewEvent["ProcessID"]));

    private void StopWatchers()
    {
        IsWatching = false;
        foreach (var traceWatcher in new[] { _processStartWatcher, _processStopWatcher })
        {
            if (traceWatcher is null) continue;
            try { traceWatcher.Stop(); }
            catch (Exception exception) when (exception is ManagementException or COMException) { }
            traceWatcher.Dispose();
        }
        _processStartWatcher = null;
        _processStopWatcher = null;
    }

    public void Dispose() => StopWatchers();
}
