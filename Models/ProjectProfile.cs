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
        var defaultProfile = new ProjectProfile
        {
            Name = DefaultProfileName,
            ProjectType = detection.IsSymfony ? ProjectType.Symfony : ProjectType.Other,
        };
        defaultProfile.Tools[ToolIds.VSCode] = ToolSelection.Enabled();
        defaultProfile.Tools[ToolIds.MySql] = ToolSelection.Enabled();
        defaultProfile.Tools[ToolIds.Browser] = ToolSelection.Enabled((ToolIds.BrowserTargetsOption, new[] { ToolIds.DefaultBrowser }));
        if (detection.IsSymfony) defaultProfile.Tools[ToolIds.SymfonyServer] = ToolSelection.Enabled();
        else defaultProfile.Tools[ToolIds.Apache] = ToolSelection.Enabled();
        if (detection.IsSymfony && detection.UsesTailwindBundle) defaultProfile.Tools[ToolIds.Tailwind] = ToolSelection.Enabled();
        return defaultProfile;
    }
}
