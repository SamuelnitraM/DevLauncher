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
                lock (_hostedServicesLock) _hostedServices.Add(hostedService);
                ServiceCreated?.Invoke(hostedService);
            }
            _launchLog.Info($"   → {serviceCommand.Title} (journal dans son onglet)");
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
