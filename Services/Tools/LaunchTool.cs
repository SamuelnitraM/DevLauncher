using DevLauncher.Models;
using DevLauncher.Services.Hosting;

namespace DevLauncher.Services.Tools;

/// <summary>Moment of the launch at which a tool starts. Stages run in ascending order and stop in descending order.</summary>
public enum LaunchStage
{
    Editor = 1,
    Infrastructure = 2,
    Workspace = 3,
    Services = 4,
    Finalization = 5,
}

/// <summary>A project tool is stopped for each launched project, a machine tool once, when a launched profile requested it.</summary>
public enum ToolScope
{
    Project,
    Machine,
}

public enum ToolStartResult
{
    Started,
    AlreadyRunning,
    Failed,
}

/// <summary>
/// Something the launcher can start and stop for a project : editor, server, terminal, browser…
/// </summary>
public abstract class LaunchTool
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract string Icon { get; }
    public abstract ToolCategory Category { get; }
    public abstract LaunchStage Stage { get; }
    public virtual ToolScope Scope => ToolScope.Project;

    /// <summary>Project types for which the tool is available, null meaning every type.</summary>
    public virtual IReadOnlyCollection<ProjectType>? SupportedProjectTypes => null;

    public virtual IReadOnlyList<ToolOptionDefinition> Options => Array.Empty<ToolOptionDefinition>();

    public string Label => $"{Icon} {DisplayName}";

    /// <summary>False when the tool is switched off in the settings : it is then neither shown nor launched.</summary>
    public virtual bool IsEnabledInSettings => true;

    public bool Supports(ProjectType projectType) => IsEnabledInSettings && (SupportedProjectTypes is null || SupportedProjectTypes.Contains(projectType));

    public virtual Task<ToolStartResult> StartAsync(ToolExecutionContext context) => Task.FromResult(ToolStartResult.Started);

    public virtual Task StopAsync(ToolExecutionContext context) => Task.CompletedTask;
}

/// <summary>
/// Long-running command (server, watcher…) whose hosting is decided by the launcher : run by DevLauncher or as a VSCode task.
/// </summary>
public abstract class ServiceTool : LaunchTool
{
    public override LaunchStage Stage => LaunchStage.Services;

    /// <summary>Returns the command to host, or null when the tool cannot start (the reason is logged).</summary>
    public abstract ServiceCommand? BuildServiceCommand(ToolExecutionContext context);
}
