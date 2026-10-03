using DevLauncher.Models;
using DevLauncher.Services.Elevation;
using DevLauncher.Services.Hosting;
using DevLauncher.Services.Tools;

namespace DevLauncher.Services;

/// <summary>
/// Orchestrates the tools of a profile : starts them stage by stage, hosts the services,
/// and stops what the launched profiles requested.
/// </summary>
public sealed class LaunchService
{
    /// <summary>Launched project, remembering where its services run.</summary>
    private sealed record LaunchedProject(ProjectProfile Profile, bool AreServicesInVSCode);

    private readonly ToolCatalog _toolCatalog;
    private readonly ProcessLauncher _processLauncher;
    private readonly VSCodeTasksServiceHost _vscodeTasksServiceHost;
    private readonly ServiceProcessHost _serviceProcessHost;
    private readonly LaunchLog _launchLog;
    private readonly Dictionary<string, LaunchedProject> _launchedProjects = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _managedMachineToolIds = new();

    public LaunchService(ToolCatalog toolCatalog, ProcessLauncher processLauncher, VSCodeTasksServiceHost vscodeTasksServiceHost, ServiceProcessHost serviceProcessHost, LaunchLog launchLog)
    {
        _toolCatalog = toolCatalog;
        _processLauncher = processLauncher;
        _vscodeTasksServiceHost = vscodeTasksServiceHost;
        _serviceProcessHost = serviceProcessHost;
        _launchLog = launchLog;
    }

    /// <summary>True when a project was launched or a machine tool is managed, and nothing was stopped since.</summary>
    public bool HasActiveEnvironment => _launchedProjects.Count > 0 || _managedMachineToolIds.Count > 0;

    // ════════════════════════════════════════════════════════
    //  LAUNCH
    // ════════════════════════════════════════════════════════

    /// <summary>
    /// Launches the tools of a profile stage by stage, after checking that the ports they need are free.
    /// Returns false when the launch is cancelled because of a port conflict.
    /// </summary>
    public async Task<bool> LaunchAsync(string projectPath, ProjectProfile profile, ILaunchObserver launchObserver)
    {
        var enabledTools = GetEnabledTools(profile).ToList();
        var launchSteps = enabledTools.Where(tool => tool is not ServiceTool).Select(tool => (tool.Stage, Label: $"{tool.Icon} {tool.DisplayName}")).ToList();
        var hasServices = enabledTools.Any(tool => tool is ServiceTool);
        if (hasServices) launchSteps.Add((LaunchStage.Services, "⚡ Services du projet"));
        var totalStepCount = launchSteps.Count + 1;
        var completedStepCount = 0;
        launchObserver.ReportProgress(new LaunchProgress(completedStepCount, totalStepCount, "🔌 Vérification des ports"));
        if (!await ResolvePortConflictsAsync(projectPath, profile, enabledTools, launchObserver)) return false;
        completedStepCount++;
        var serviceCommands = BuildServiceCommands(projectPath, profile, enabledTools);
        var areServicesInVSCode = AppSettings.HostServicesInVSCode && serviceCommands.Count > 0 && enabledTools.Any(tool => tool.Id == ToolIds.VSCode);
        _launchedProjects[projectPath] = new LaunchedProject(profile, areServicesInVSCode);
        // VSCode reads tasks.json when the folder opens, so the services are prepared before the editor starts.
        if (areServicesInVSCode) _vscodeTasksServiceHost.PrepareServices(projectPath, serviceCommands);
        foreach (var launchStage in Enum.GetValues<LaunchStage>())
        {
            if (launchStage == LaunchStage.Services)
            {
                if (!hasServices) continue;
                launchObserver.ReportProgress(new LaunchProgress(completedStepCount, totalStepCount, "⚡ Services du projet"));
                if (!areServicesInVSCode && serviceCommands.Count > 0)
                {
                    await StopStaleServiceInstancesAsync(projectPath, profile, enabledTools, serviceCommands);
                    _serviceProcessHost.StartServices(projectPath, serviceCommands);
                }
                completedStepCount++;
                continue;
            }
            foreach (var tool in enabledTools.Where(tool => tool.Stage == launchStage && tool is not ServiceTool))
            {
                launchObserver.ReportProgress(new LaunchProgress(completedStepCount, totalStepCount, $"{tool.Icon} {tool.DisplayName}"));
                var toolStartResult = await tool.StartAsync(CreateContext(projectPath, profile, tool));
                // A machine tool requested by the profile is managed even when it was already running : « Tout arrêter » stops it.
                if (toolStartResult != ToolStartResult.Failed && tool.Scope == ToolScope.Machine) _managedMachineToolIds.Add(tool.Id);
                completedStepCount++;
            }
        }
        launchObserver.ReportProgress(new LaunchProgress(totalStepCount, totalStepCount, "✅ Terminé"));
        return true;
    }

    /// <summary>
    /// Finds the ports needed by the tools that another program holds and lets the observer decide for each of them.
    /// Returns false when the observer cancels the launch.
    /// </summary>
    private async Task<bool> ResolvePortConflictsAsync(string projectPath, ProjectProfile profile, IEnumerable<LaunchTool> enabledTools, ILaunchObserver launchObserver)
    {
        foreach (var portConflict in FindPortConflicts(projectPath, profile, enabledTools))
        {
            _launchLog.Error($"⚠️ Port {portConflict.Port} ({portConflict.ToolName}) occupé par {portConflict.OwnerProcessName} (PID {portConflict.OwnerProcessId})");
            switch (launchObserver.ResolvePortConflict(portConflict))
            {
                case PortConflictDecision.CancelLaunch:
                    _launchLog.Info("⏹ Lancement annulé");
                    return false;
                case PortConflictDecision.StopOwner:
                    await _processLauncher.StopProcessByIdAsync(portConflict.OwnerProcessId, portConflict.OwnerProcessName);
                    break;
                default:
                    _launchLog.Info($"   → Lancement malgré le conflit : {portConflict.ToolName} risque de ne pas démarrer");
                    break;
            }
        }
        return true;
    }

    private IEnumerable<PortConflict> FindPortConflicts(string projectPath, ProjectProfile profile, IEnumerable<LaunchTool> enabledTools)
    {
        var checkedPorts = new HashSet<int>();
        foreach (var tool in enabledTools)
        {
            foreach (var requiredPort in tool.GetRequiredPorts(CreateContext(projectPath, profile, tool)))
            {
                if (!checkedPorts.Add(requiredPort.Port) || PortOwnerLocator.FindListeningProcessId(requiredPort.Port) is not { } ownerProcessId) continue;
                var ownerProcessName = ProcessHelper.TryGetProcessName(ownerProcessId) ?? "processus inconnu";
                if (requiredPort.AcceptedOwnerProcessNames.Contains(ownerProcessName, StringComparer.OrdinalIgnoreCase)) continue;
                yield return new PortConflict(tool.DisplayName, requiredPort.Port, ownerProcessId, ownerProcessName);
            }
        }
    }

    private List<ServiceCommand> BuildServiceCommands(string projectPath, ProjectProfile profile, IEnumerable<LaunchTool> enabledTools)
    {
        var serviceTools = enabledTools.OfType<ServiceTool>().ToList();
        if (serviceTools.Count == 0) return new List<ServiceCommand>();
        _launchLog.Info("⚡ Préparation des services…");
        return serviceTools
            .Select(serviceTool => serviceTool.BuildServiceCommand(CreateContext(projectPath, profile, serviceTool)))
            .OfType<ServiceCommand>()
            .ToList();
    }

    /// <summary>
    /// Stops the instances of the services about to start that DevLauncher does not own (left by a previous session
    /// or started by hand), so that the new instances can take the port and the files they need.
    /// </summary>
    private async Task StopStaleServiceInstancesAsync(string projectPath, ProjectProfile profile, IEnumerable<LaunchTool> enabledTools, IEnumerable<ServiceCommand> serviceCommands)
    {
        var serviceToolIdsToStart = serviceCommands
            .Where(serviceCommand => !_serviceProcessHost.IsServiceRunning(projectPath, serviceCommand.ToolId))
            .Select(serviceCommand => serviceCommand.ToolId)
            .ToHashSet();
        var serviceToolsToClean = enabledTools.OfType<ServiceTool>().Where(serviceTool => serviceToolIdsToStart.Contains(serviceTool.Id)).ToList();
        if (serviceToolsToClean.Count == 0) return;
        _launchLog.Info("🧹 Arrêt des instances précédentes non gérées par DevLauncher…");
        foreach (var serviceTool in serviceToolsToClean)
            await serviceTool.StopAsync(CreateContext(projectPath, profile, serviceTool));
    }

    // ════════════════════════════════════════════════════════
    //  STOP
    // ════════════════════════════════════════════════════════

    public async Task StopAllAsync()
    {
        _vscodeTasksServiceHost.RestorePendingTasksFiles();
        foreach (var (projectPath, launchedProject) in _launchedProjects.ToList())
        {
            if (!launchedProject.AreServicesInVSCode) await _serviceProcessHost.StopProjectServicesAsync(projectPath);
            // Services run as VSCode tasks are not owned by DevLauncher : their tools stop them.
            var projectToolsToStop = GetEnabledTools(launchedProject.Profile)
                .Where(tool => tool.Scope == ToolScope.Project && (tool is not ServiceTool || launchedProject.AreServicesInVSCode))
                .OrderByDescending(tool => tool.Stage);
            foreach (var projectTool in projectToolsToStop)
                await projectTool.StopAsync(CreateContext(projectPath, launchedProject.Profile, projectTool));
        }
        foreach (var machineTool in _toolCatalog.Tools.Where(tool => _managedMachineToolIds.Contains(tool.Id)))
            await machineTool.StopAsync(CreateContext(string.Empty, new ProjectProfile(), machineTool));
        _launchedProjects.Clear();
        _managedMachineToolIds.Clear();
        _launchLog.Info("✅ Tout est arrêté !");
    }

    /// <summary>Restores the project files modified for the launch and kills the services owned by DevLauncher. Called when the application closes.</summary>
    public void Shutdown()
    {
        _vscodeTasksServiceHost.RestorePendingTasksFiles();
        _serviceProcessHost.KillAll();
    }

    // ════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════

    private IEnumerable<LaunchTool> GetEnabledTools(ProjectProfile profile)
        => _toolCatalog.Tools.Where(tool => profile.IsToolEnabled(tool.Id) && tool.Supports(profile.ProjectType));

    private ToolExecutionContext CreateContext(string projectPath, ProjectProfile profile, LaunchTool tool) => new()
    {
        ProjectPath = projectPath,
        Profile = profile,
        Selection = profile.GetToolSelection(tool.Id),
        ProcessLauncher = _processLauncher,
        Log = _launchLog,
        ServiceHost = _serviceProcessHost,
        RunsUnelevated = ElevationPolicy.RunsUnelevated(tool),
    };
}
