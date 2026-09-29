namespace DevLauncher.Services;

/// <summary>
/// Tracks whether Apache, MySQL and FileZilla are running, from the process start and stop events,
/// and raises StatusChanged to update the UI.
/// </summary>
public sealed class ServiceMonitor : IDisposable
{
    private static readonly Dictionary<string, string> _serviceNamesByProcessName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["httpd"] = "Apache",
        ["mysqld"] = "MySQL",
        ["FileZillaServer"] = "FileZilla",
    };

    private readonly ProcessEventWatcher _processEventWatcher;
    private readonly Dictionary<string, HashSet<int>> _runningProcessIdsByService = new();
    private readonly object _stateLock = new();

    /// <summary>
    /// Raised when the status of a service is published.
    /// Parameters : service name ("Apache" | "MySQL" | "FileZilla"), is it running ?
    /// </summary>
    public event Action<string, bool>? StatusChanged;

    public ServiceMonitor(ProcessEventWatcher processEventWatcher)
    {
        _processEventWatcher = processEventWatcher;
        _processEventWatcher.ProcessStarted += OnProcessStarted;
        _processEventWatcher.ProcessStopped += OnProcessStopped;
    }

    /// <summary>Reads the running processes and publishes the status of every service.</summary>
    public void RefreshStatus()
    {
        foreach (var (processName, serviceName) in _serviceNamesByProcessName)
        {
            var runningProcessIds = ProcessHelper.GetProcessIds(processName);
            lock (_stateLock) _runningProcessIdsByService[serviceName] = runningProcessIds;
            StatusChanged?.Invoke(serviceName, runningProcessIds.Count > 0);
        }
    }

    private void OnProcessStarted(int processId, string processName)
    {
        if (!_serviceNamesByProcessName.TryGetValue(processName, out var serviceName)) return;
        bool serviceJustStarted;
        lock (_stateLock)
        {
            var runningProcessIds = GetRunningProcessIds(serviceName);
            serviceJustStarted = runningProcessIds.Add(processId) && runningProcessIds.Count == 1;
        }
        if (serviceJustStarted) StatusChanged?.Invoke(serviceName, true);
    }

    private void OnProcessStopped(int processId)
    {
        var stoppedServiceNames = new List<string>();
        lock (_stateLock)
        {
            foreach (var (serviceName, runningProcessIds) in _runningProcessIdsByService)
                if (runningProcessIds.Remove(processId) && runningProcessIds.Count == 0) stoppedServiceNames.Add(serviceName);
        }
        foreach (var serviceName in stoppedServiceNames) StatusChanged?.Invoke(serviceName, false);
    }

    private HashSet<int> GetRunningProcessIds(string serviceName)
    {
        if (!_runningProcessIdsByService.TryGetValue(serviceName, out var runningProcessIds))
        {
            runningProcessIds = new HashSet<int>();
            _runningProcessIdsByService[serviceName] = runningProcessIds;
        }
        return runningProcessIds;
    }

    public void Dispose()
    {
        _processEventWatcher.ProcessStarted -= OnProcessStarted;
        _processEventWatcher.ProcessStopped -= OnProcessStopped;
    }
}
