using DevLauncher.Models;
using DevLauncher.Services.Hosting;
using DevLauncher.Services.Tools;

namespace DevLauncher.Services;

/// <summary>
/// Orchestrates the tools of a profile : starts them stage by stage, hosts the services,
/// and stops only what was started by the launcher.
/// </summary>
public sealed class LaunchService
{
    private readonly ToolCatalog _toolCatalog;
    private readonly ProcessLauncher _processLauncher;
    private readonly VSCodeTasksServiceHost _vscodeTasksServiceHost;
    private readonly WindowsTerminalServiceHost _windowsTerminalServiceHost;
    private readonly LaunchLog _launchLog;
    private readonly Dictionary<string, ProjectProfile> _launchedProjects = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _machineToolIdsStartedByLauncher = new();

    public LaunchService(ToolCatalog toolCatalog, ProcessLauncher processLauncher, VSCodeTasksServiceHost vscodeTasksServiceHost, WindowsTerminalServiceHost windowsTerminalServiceHost, LaunchLog launchLog)
    {
        _toolCatalog = toolCatalog;
        _processLauncher = processLauncher;
        _vscodeTasksServiceHost = vscodeTasksServiceHost;
        _windowsTerminalServiceHost = windowsTerminalServiceHost;
        _launchLog = launchLog;
    }

    // ════════════════════════════════════════════════════════
    //  LAUNCH
    // ════════════════════════════════════════════════════════

    public async Task LaunchAsync(string projectPath, ProjectProfile profile)
    {
        _launchedProjects[projectPath] = profile;
        var enabledTools = GetEnabledTools(profile).ToList();
        var serviceCommands = BuildServiceCommands(projectPath, profile, enabledTools);
        // VSCode reads tasks.json when the folder opens, so the services are prepared before the editor starts.
        var hostServicesInVSCode = serviceCommands.Count > 0 && enabledTools.Any(tool => tool.Id == ToolIds.VSCode);
        if (hostServicesInVSCode) _vscodeTasksServiceHost.PrepareServices(projectPath, serviceCommands);
        foreach (var launchStage in Enum.GetValues<LaunchStage>())
        {
            if (launchStage == LaunchStage.Services)
            {
                if (!hostServicesInVSCode && serviceCommands.Count > 0) _windowsTerminalServiceHost.StartServices(serviceCommands);
                continue;
            }
            foreach (var tool in enabledTools.Where(tool => tool.Stage == launchStage))
            {
                var toolStartResult = await tool.StartAsync(CreateContext(projectPath, profile, tool));
                if (toolStartResult == ToolStartResult.Started && tool.Scope == ToolScope.Machine) _machineToolIdsStartedByLauncher.Add(tool.Id);
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
        foreach (var (projectPath, profile) in _launchedProjects.ToList())
        {
            var projectTools = GetEnabledTools(profile)
                .Where(tool => tool.Scope == ToolScope.Project)
                .OrderByDescending(tool => tool.Stage);
            foreach (var projectTool in projectTools)
                await projectTool.StopAsync(CreateContext(projectPath, profile, projectTool));
        }
        foreach (var machineTool in _toolCatalog.Tools.Where(tool => _machineToolIdsStartedByLauncher.Contains(tool.Id)))
            await machineTool.StopAsync(CreateContext(string.Empty, new ProjectProfile(), machineTool));
        _launchedProjects.Clear();
        _machineToolIdsStartedByLauncher.Clear();
        _launchLog.Info("✅ Tout est arrêté !");
    }

    /// <summary>Restores the project files modified for the launch. Called when the application closes.</summary>
    public void RestoreProjectFiles() => _vscodeTasksServiceHost.RestorePendingTasksFiles();

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
