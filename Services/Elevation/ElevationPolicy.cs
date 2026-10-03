using DevLauncher.Services.Tools;

namespace DevLauncher.Services.Elevation;

/// <summary>Families of applications that can be started with the rights of the standard user, chosen in the settings.</summary>
public static class ElevationPolicy
{
    public const string VSCodeFamily = "vscode";
    public const string VisualStudioFamily = "visual-studio";
    public const string TerminalFamily = "terminal";
    public const string BrowserFamily = "browser";
    public const string AssistantsFamily = "assistants";
    public const string CliAgentsFamily = "cli-agents";

    /// <summary>Families offered in the settings, with their label.</summary>
    public static IReadOnlyList<(string Family, string Label)> Families { get; } = new[]
    {
        (VSCodeFamily, "💻 VSCode"),
        (VisualStudioFamily, "🟣 Visual Studio"),
        (TerminalFamily, "🖥️ Terminal"),
        (BrowserFamily, "🌍 Navigateur"),
        (AssistantsFamily, "🤖 Assistants IA (navigateur et application)"),
        (CliAgentsFamily, "⌨️ Agents en ligne de commande"),
    };

    /// <summary>True when the family of the tool is set to start without the administrator rights.</summary>
    public static bool RunsUnelevated(LaunchTool tool)
    {
        var family = tool switch
        {
            ChatAssistantTool => AssistantsFamily,
            CliAgentTool => CliAgentsFamily,
            VSCodeTool => VSCodeFamily,
            VisualStudioTool => VisualStudioFamily,
            TerminalTool => TerminalFamily,
            BrowserTool => BrowserFamily,
            _ => null,
        };
        return family is not null && AppSettings.UnelevatedApplications.Contains(family, StringComparer.OrdinalIgnoreCase);
    }
}
