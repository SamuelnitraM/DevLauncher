using System.IO;
using DevLauncher.Models;

namespace DevLauncher.Services.Assistants;

/// <summary>Built-in description of a conversation assistant, opened in the browser or in its desktop application.</summary>
/// <param name="ApplicationNames">Names of the desktop application in the Start menu.</param>
/// <param name="ApplicationCandidates">Usual install locations of the desktop application, environment variables allowed.</param>
public sealed record AssistantDefinition(
    string Id,
    string DisplayName,
    string Icon,
    string DefaultWebUrl,
    IReadOnlyList<string> ApplicationNames,
    IReadOnlyList<string> ApplicationCandidates,
    bool IsEnabledByDefault);

/// <summary>Assistants known by DevLauncher and access to their settings.</summary>
public static class AssistantCatalog
{
    public const string ClaudeId = "claude";

    public static IReadOnlyList<AssistantDefinition> Definitions { get; } = new[]
    {
        new AssistantDefinition(ClaudeId, "Claude", "🟠", "https://claude.ai/new",
            new[] { "Claude" },
            new[]
            {
                @"%LOCALAPPDATA%\AnthropicClaude\claude.exe",
                @"%LOCALAPPDATA%\Programs\Claude\Claude.exe",
                @"%LOCALAPPDATA%\Microsoft\WindowsApps\claude.exe",
            },
            IsEnabledByDefault: true),
        new AssistantDefinition("chatgpt", "ChatGPT", "🟢", "https://chatgpt.com/",
            new[] { "ChatGPT" },
            new[]
            {
                @"%LOCALAPPDATA%\Microsoft\WindowsApps\ChatGPT.exe",
                @"%LOCALAPPDATA%\Programs\ChatGPT\ChatGPT.exe",
            },
            IsEnabledByDefault: false),
        new AssistantDefinition("gemini", "Gemini", "🔵", "https://gemini.google.com/app",
            new[] { "Gemini" }, Array.Empty<string>(), IsEnabledByDefault: false),
        new AssistantDefinition("mistral", "Mistral", "🔶", "https://chat.mistral.ai/chat",
            new[] { "Le Chat", "Mistral" }, Array.Empty<string>(), IsEnabledByDefault: false),
        new AssistantDefinition("perplexity", "Perplexity", "🔎", "https://www.perplexity.ai/",
            new[] { "Perplexity" }, Array.Empty<string>(), IsEnabledByDefault: false),
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

    /// <summary>Returns the configured application target, or else the detected one.</summary>
    public static string? ResolveApplicationTarget(string assistantId)
    {
        var configuredTarget = GetSettings(assistantId).ApplicationTarget;
        return string.IsNullOrWhiteSpace(configuredTarget) ? DetectApplicationTarget(GetDefinition(assistantId)) : configuredTarget.Trim();
    }

    /// <summary>
    /// Detects the desktop application : first the usual install locations, then the Start menu shortcuts and applications,
    /// which also cover the Microsoft Store and MSIX installations.
    /// </summary>
    public static string? DetectApplicationTarget(AssistantDefinition assistantDefinition)
        => assistantDefinition.ApplicationCandidates.Select(Environment.ExpandEnvironmentVariables).FirstOrDefault(File.Exists)
           ?? (assistantDefinition.ApplicationNames.Count > 0 ? InstalledApplicationLocator.FindApplication(assistantDefinition.ApplicationNames) : null);

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
