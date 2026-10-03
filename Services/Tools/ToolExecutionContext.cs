using System.IO;
using DevLauncher.Models;

namespace DevLauncher.Services.Tools;

/// <summary>Everything a tool needs to start or stop for a given project.</summary>
public sealed class ToolExecutionContext
{
    public required string ProjectPath { get; init; }
    public required ProjectProfile Profile { get; init; }
    public required ToolSelection Selection { get; init; }
    public required ProcessLauncher ProcessLauncher { get; init; }
    public required LaunchLog Log { get; init; }
    public required Hosting.ServiceProcessHost ServiceHost { get; init; }

    /// <summary>The applications of this tool start with the rights of the standard user.</summary>
    public bool RunsUnelevated { get; init; }

    public string ProjectName => Path.GetFileName(ProjectPath);

    public IReadOnlyList<string> GetOptionValues(string optionKey) => Selection.GetOptionValues(optionKey);
}
