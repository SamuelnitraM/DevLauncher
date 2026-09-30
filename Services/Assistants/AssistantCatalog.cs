using System.IO;
using DevLauncher.Models;

namespace DevLauncher.Services.Assistants;

public enum AssistantKind
{
    /// <summary>Conversation assistant, opened in the browser or in its desktop application.</summary>
    Chat,

    /// <summary>Command line agent, opened in a terminal in the project folder.</summary>
    CommandLine,
}

/// <summary>Built-in description of an assistant.</summary>
/// <param name="ApplicationCandidates">Usual install locations of the desktop application, environment variables allowed.</param>
public sealed record AssistantDefinition(
    string Id,
    string DisplayName,
    string Icon,
    AssistantKind Kind,
    string DefaultWebUrl,
    IReadOnlyList<string> ApplicationCandidates,
    bool IsEnabledByDefault);

/// <summary>Assistants known by DevLauncher and access to their settings.</summary>
public static class AssistantCatalog
{
    public const string ClaudeId = "claude";
    public const string ClaudeCodeId = "claude-code";

    public static IReadOnlyList<AssistantDefinition> Definitions { get; } = new[]
    {
        new AssistantDefinition(ClaudeId, "Claude", "🟠", AssistantKind.Chat, "https://claude.ai/new",
            new[]
            {
                @"%LOCALAPPDATA%\AnthropicClaude\claude.exe",
                @"%LOCALAPPDATA%\Programs\Claude\Claude.exe",
                @"%LOCALAPPDATA%\Microsoft\WindowsApps\claude.exe",
            },
            IsEnabledByDefault: true),
        new AssistantDefinition("chatgpt", "ChatGPT", "🟢", AssistantKind.Chat, "https://chatgpt.com/",
            new[]
            {
                @"%LOCALAPPDATA%\Microsoft\WindowsApps\ChatGPT.exe",
                @"%LOCALAPPDATA%\Programs\ChatGPT\ChatGPT.exe",
            },
            IsEnabledByDefault: false),
        new AssistantDefinition("gemini", "Gemini", "🔵", AssistantKind.Chat, "https://gemini.google.com/app",
            Array.Empty<string>(), IsEnabledByDefault: false),
        new AssistantDefinition("mistral", "Le Chat (Mistral)", "🔶", AssistantKind.Chat, "https://chat.mistral.ai/chat",
            Array.Empty<string>(), IsEnabledByDefault: false),
        new AssistantDefinition("perplexity", "Perplexity", "🔎", AssistantKind.Chat, "https://www.perplexity.ai/",
            Array.Empty<string>(), IsEnabledByDefault: false),
        new AssistantDefinition(ClaudeCodeId, "Claude Code", "⌨️", AssistantKind.CommandLine, string.Empty,
            Array.Empty<string>(), IsEnabledByDefault: true),
    };

    public static AssistantDefinition GetDefinition(string assistantId) => Definitions.First(definition => definition.Id == assistantId);

    /// <summary>Settings of every known assistant : the saved ones completed with the defaults of the missing ones.</summary>
    public static List<AssistantSettings> MergeWithDefaults(IEnumerable<AssistantSettings>? savedAssistantSettings)
    {
        var savedSettingsById = (savedAssistantSettings ?? Enumerable.Empty<AssistantSettings>())
            .Where(settings => !string.IsNullOrWhiteSpace(settings.Id))
            .GroupBy(settings => settings.Id)
            .ToDictionary(group => group.Key, group => group.First());
        return Definitions
            .Select(definition => savedSettingsById.TryGetValue(definition.Id, out var savedSettings)
                ? Normalize(definition, savedSettings)
                : CreateDefaultSettings(definition))
            .ToList();
    }

    public static AssistantSettings GetSettings(string assistantId)
        => AppSettings.Assistants.FirstOrDefault(settings => settings.Id == assistantId) ?? CreateDefaultSettings(GetDefinition(assistantId));

    /// <summary>Returns the configured application target, or the first usual install location that exists.</summary>
    public static string? ResolveApplicationTarget(string assistantId)
    {
        var configuredTarget = GetSettings(assistantId).ApplicationTarget;
        if (!string.IsNullOrWhiteSpace(configuredTarget)) return configuredTarget.Trim();
        return GetDefinition(assistantId).ApplicationCandidates
            .Select(Environment.ExpandEnvironmentVariables)
            .FirstOrDefault(File.Exists);
    }

    private static AssistantSettings CreateDefaultSettings(AssistantDefinition definition) => new()
    {
        Id = definition.Id,
        IsEnabled = definition.IsEnabledByDefault,
        DefaultMode = AssistantModes.Browser,
        WebUrl = definition.DefaultWebUrl,
    };

    private static AssistantSettings Normalize(AssistantDefinition definition, AssistantSettings savedSettings)
    {
        if (string.IsNullOrWhiteSpace(savedSettings.WebUrl)) savedSettings.WebUrl = definition.DefaultWebUrl;
        if (savedSettings.DefaultMode is not (AssistantModes.Browser or AssistantModes.Application)) savedSettings.DefaultMode = AssistantModes.Browser;
        savedSettings.Projects ??= new List<AssistantProject>();
        savedSettings.Projects.RemoveAll(project => string.IsNullOrWhiteSpace(project.Name) || string.IsNullOrWhiteSpace(project.Url));
        return savedSettings;
    }
}
