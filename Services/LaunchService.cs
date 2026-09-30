using DevLauncher.Models;
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

    public async Task LaunchAsync(string projectPath, ProjectProfile profile)
    {
        var enabledTools = GetEnabledTools(profile).ToList();
        var serviceCommands = BuildServiceCommands(projectPath, profile, enabledTools);
        var areServicesInVSCode = AppSettings.HostServicesInVSCode && serviceCommands.Count > 0 && enabledTools.Any(tool => tool.Id == ToolIds.VSCode);
        _launchedProjects[projectPath] = new LaunchedProject(profile, areServicesInVSCode);
        // VSCode reads tasks.json when the folder opens, so the services are prepared before the editor starts.
        if (areServicesInVSCode) _vscodeTasksServiceHost.PrepareServices(projectPath, serviceCommands);
        foreach (var launchStage in Enum.GetValues<LaunchStage>())
        {
            if (launchStage == LaunchStage.Services)
            {
                if (!areServicesInVSCode && serviceCommands.Count > 0) _serviceProcessHost.StartServices(projectPath, serviceCommands);
                continue;
            }
            foreach (var tool in enabledTools.Where(tool => tool.Stage == launchStage))
            {
                var toolStartResult = await tool.StartAsync(CreateContext(projectPath, profile, tool));
                // A machine tool requested by the profile is managed even when it was already running : « Tout arrêter » stops it.
                if (toolStartResult != ToolStartResult.Failed && tool.Scope == ToolScope.Machine) _managedMachineToolIds.Add(tool.Id);
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
    };
}
