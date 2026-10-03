using System.ComponentModel;
using System.IO;

namespace DevLauncher.Services.Hosting;

/// <summary>
/// Runs the services as processes owned by DevLauncher, one per project and tool.
/// </summary>
public sealed class ServiceProcessHost
{
    private readonly LaunchLog _launchLog;
    private readonly KillOnCloseJob? _killOnCloseJob;
    private readonly List<HostedService> _hostedServices = new();
    private readonly object _hostedServicesLock = new();
    private readonly Dictionary<string, string> _announcedUrlsByProject = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TaskCompletionSource<string>> _urlWaitersByProject = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _announcedUrlsLock = new();

    public ServiceProcessHost(LaunchLog launchLog)
    {
        _launchLog = launchLog;
        try
        {
            _killOnCloseJob = new KillOnCloseJob();
        }
        catch (Win32Exception exception)
        {
            _launchLog.Error($"⚠️ Les services ne pourront pas être arrêtés automatiquement si DevLauncher est tué : {exception.Message}");
        }
    }

    public bool IsServiceRunning(string projectPath, string toolId) => FindService(projectPath, toolId) is { IsRunning: true };

    /// <summary>Raised on the launching thread when a new service is created, before it starts.</summary>
    public event Action<HostedService>? ServiceCreated;

    public void StartServices(string projectPath, IReadOnlyList<ServiceCommand> serviceCommands)
    {
        foreach (var serviceCommand in serviceCommands)
        {
            var hostedService = FindService(projectPath, serviceCommand.ToolId);
            if (hostedService is { IsRunning: true })
            {
                _launchLog.Info($"   → {serviceCommand.Title} déjà en cours — ignoré");
                continue;
            }
            if (hostedService is null)
            {
                hostedService = new HostedService(projectPath, serviceCommand, _killOnCloseJob);
                var serviceTitle = $"{serviceCommand.Title} · {hostedService.ProjectName}";
                hostedService.OutputReceived += (outputLine, isError) => _launchLog.ServiceOutput(serviceTitle, outputLine, isError);
                if (serviceCommand.AnnouncesApplicationUrl) hostedService.OutputReceived += (outputLine, _) => OnWebServerOutput(projectPath, outputLine);
                lock (_hostedServicesLock) _hostedServices.Add(hostedService);
                ServiceCreated?.Invoke(hostedService);
            }
            // A restarted web server may announce another port : the previous URL is forgotten.
            if (serviceCommand.AnnouncesApplicationUrl) ForgetAnnouncedUrl(projectPath);
            _launchLog.Info($"   → {serviceCommand.Title} (journal dans son onglet)");
            _launchLog.Detail($"Service : {serviceCommand.Executable} {string.Join(' ', serviceCommand.Arguments)} (dossier {serviceCommand.WorkingDirectory})");
            if (!hostedService.Start()) _launchLog.Error($"❌ {serviceCommand.Title} n'a pas pu démarrer : voir son onglet");
        }
    }

    public async Task StopProjectServicesAsync(string projectPath)
    {
        foreach (var hostedService in GetServices().Where(service => service.IsRunning && IsSamePath(service.ProjectPath, projectPath)))
        {
            _launchLog.Info($"⏹ Arrêt de {hostedService.Command.Title} ({hostedService.ProjectName})…");
            await hostedService.StopAsync();
        }
    }

    /// <summary>
    /// Returns the URL announced by the web server of the project as soon as it is printed,
    /// or null when nothing is announced before the timeout.
    /// </summary>
    public async Task<string?> WaitForAnnouncedUrlAsync(string projectPath, TimeSpan timeout)
    {
        Task<string> announcedUrlTask;
        lock (_announcedUrlsLock)
        {
            if (_announcedUrlsByProject.TryGetValue(projectPath, out var announcedUrl)) return announcedUrl;
            if (!_urlWaitersByProject.TryGetValue(projectPath, out var urlWaiter))
            {
                urlWaiter = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                _urlWaitersByProject[projectPath] = urlWaiter;
            }
            announcedUrlTask = urlWaiter.Task;
        }
        try
        {
            return await announcedUrlTask.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    private void OnWebServerOutput(string projectPath, string outputLine)
    {
        var announcedUrl = ApplicationUrlParser.TryExtractLocalUrl(outputLine);
        if (announcedUrl is null) return;
        TaskCompletionSource<string>? urlWaiter;
        lock (_announcedUrlsLock)
        {
            if (_announcedUrlsByProject.ContainsKey(projectPath)) return;
            _announcedUrlsByProject[projectPath] = announcedUrl;
            _urlWaitersByProject.Remove(projectPath, out urlWaiter);
        }
        urlWaiter?.TrySetResult(announcedUrl);
    }

    private void ForgetAnnouncedUrl(string projectPath)
    {
        lock (_announcedUrlsLock) _announcedUrlsByProject.Remove(projectPath);
    }

    /// <summary>Kills every running service without waiting. Used when the application closes.</summary>
    public void KillAll()
    {
        foreach (var hostedService in GetServices()) hostedService.Kill();
    }

    /// <summary>Forgets a stopped service, so that its next start creates a fresh one.</summary>
    public void Remove(HostedService hostedService)
    {
        if (hostedService.IsRunning) return;
        lock (_hostedServicesLock) _hostedServices.Remove(hostedService);
    }

    private HostedService? FindService(string projectPath, string toolId)
        => GetServices().FirstOrDefault(service => service.Command.ToolId == toolId && IsSamePath(service.ProjectPath, projectPath));

    private List<HostedService> GetServices()
    {
        lock (_hostedServicesLock) return _hostedServices.ToList();
    }

    private static bool IsSamePath(string firstPath, string secondPath)
        => string.Equals(Path.TrimEndingDirectorySeparator(firstPath), Path.TrimEndingDirectorySeparator(secondPath), StringComparison.OrdinalIgnoreCase);
}
