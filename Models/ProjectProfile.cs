namespace DevLauncher.Models;

/// <summary>
/// Saved launch preset of a project : project type and state of every tool.
/// Also used as the launch request passed to the launch service.
/// </summary>
public class ProjectProfile
{
    public const string DefaultProfileName = "Défaut";

    public string Name { get; set; } = DefaultProfileName;

    public ProjectType ProjectType { get; set; }

    /// <summary>State of the tools by tool identifier (see ToolIds).</summary>
    public Dictionary<string, ToolSelection> Tools { get; set; } = new();

    public bool IsToolEnabled(string toolId) => Tools.TryGetValue(toolId, out var toolSelection) && toolSelection.IsEnabled;

    public ToolSelection GetToolSelection(string toolId)
        => Tools.TryGetValue(toolId, out var toolSelection) ? toolSelection : new ToolSelection();

    /// <summary>Builds the initial profile of a project from what was detected in its folder.</summary>
    public static ProjectProfile CreateDefault(ProjectDetection detection)
    {
        var defaultProfile = new ProjectProfile { Name = DefaultProfileName, ProjectType = detection.ProjectType };
        void EnableTool(string toolId) => defaultProfile.Tools[toolId] = ToolSelection.Enabled();
        EnableTool(ToolIds.VSCode);
        defaultProfile.Tools[ToolIds.Browser] = ToolSelection.Enabled((ToolIds.BrowserTargetsOption, new[] { ToolIds.DefaultBrowser }));
        switch (detection.ProjectType)
        {
            case ProjectType.Symfony:
                EnableTool(ToolIds.MySql);
                EnableTool(ToolIds.SymfonyServer);
                if (detection.UsesTailwindBundle) EnableTool(ToolIds.Tailwind);
                break;
            case ProjectType.Laravel:
                EnableTool(ToolIds.MySql);
                EnableTool(ToolIds.LaravelServer);
                if (detection.HasPackageJson) EnableTool(ToolIds.NpmScript);
                break;
            case ProjectType.Node:
                EnableTool(ToolIds.NpmScript);
                break;
            case ProjectType.Django:
                EnableTool(ToolIds.DjangoServer);
                break;
            case ProjectType.DotNet:
                EnableTool(ToolIds.DotNetWatch);
                break;
            default:
                EnableTool(ToolIds.Apache);
                EnableTool(ToolIds.MySql);
                break;
        }
        return defaultProfile;
    }
}
