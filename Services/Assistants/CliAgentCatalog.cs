namespace DevLauncher.Services.Assistants;

/// <summary>Coding agent run in a terminal : its command, its installation page, and how it resumes the last conversation.</summary>
/// <param name="ContinueArguments">Arguments resuming the last conversation of the folder, null when the agent cannot.</param>
public sealed record CliAgentDefinition(string Id, string DisplayName, string Icon, string Command, string InstallUrl, IReadOnlyList<string>? ContinueArguments);

/// <summary>User settings of a command line agent.</summary>
public class CliAgentSettings
{
    public string Id { get; set; } = string.Empty;

    /// <summary>An enabled agent appears in the AI assistants of the main window.</summary>
    public bool IsEnabled { get; set; }
}

/// <summary>Command line agents known by DevLauncher. They are offered only once enabled in the settings, where their installation is checked.</summary>
public static class CliAgentCatalog
{
    public static IReadOnlyList<CliAgentDefinition> Definitions { get; } = new[]
    {
        new CliAgentDefinition("claude-code", "Claude Code", "🟧", "claude", "https://docs.claude.com/en/docs/claude-code/setup", new[] { "--continue" }),
        new CliAgentDefinition("codex", "Codex CLI", "🟩", "codex", "https://github.com/openai/codex", new[] { "resume", "--last" }),
        new CliAgentDefinition("gemini-cli", "Gemini CLI", "🔷", "gemini", "https://github.com/google-gemini/gemini-cli", null),
        new CliAgentDefinition("aider", "Aider", "🟨", "aider", "https://aider.chat/docs/install.html", null),
    };

    public static CliAgentDefinition GetDefinition(string agentId) => Definitions.First(definition => definition.Id == agentId);

    /// <summary>Settings of every known agent : the saved ones, and disabled settings for the others.</summary>
    public static List<CliAgentSettings> MergeWithDefaults(IEnumerable<CliAgentSettings>? savedAgentSettings)
    {
        var savedSettingsById = (savedAgentSettings ?? Enumerable.Empty<CliAgentSettings>())
            .Where(settings => !string.IsNullOrWhiteSpace(settings.Id))
            .GroupBy(settings => settings.Id)
            .ToDictionary(group => group.Key, group => group.First());
        return Definitions
            .Select(definition => savedSettingsById.GetValueOrDefault(definition.Id) ?? new CliAgentSettings { Id = definition.Id })
            .ToList();
    }

    public static bool IsEnabled(string agentId) => AppSettings.CliAgents.Any(settings => settings.Id == agentId && settings.IsEnabled);

    /// <summary>Returns the full path of the agent command found in the PATH, or null when it is not installed.</summary>
    public static string? FindInstalledCommand(CliAgentDefinition definition) => ExecutableLocator.FindInPath(definition.Command);
}
