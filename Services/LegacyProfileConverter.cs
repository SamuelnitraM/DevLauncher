using System.Text.Json.Nodes;
using DevLauncher.Models;

namespace DevLauncher.Services;

/// <summary>
/// Converts the profiles saved with one boolean property per option into tool-based profiles.
/// </summary>
public static class LegacyProfileConverter
{
    private static readonly (string LegacyPropertyName, string ToolId)[] _legacyToolFlags =
    {
        ("openVSCode", ToolIds.VSCode),
        ("openVisualStudio", ToolIds.VisualStudio),
        ("showXamppPanel", ToolIds.XamppPanel),
        ("startApache", ToolIds.Apache),
        ("startMySQL", ToolIds.MySql),
        ("startFileZilla", ToolIds.FileZilla),
        ("startSymfonyServer", ToolIds.SymfonyServer),
        ("startTailwind", ToolIds.Tailwind),
        ("startMercure", ToolIds.Mercure),
        ("openTerminal", ToolIds.Terminal),
        ("openBrowser", ToolIds.Browser),
    };

    private static readonly (string LegacyPropertyName, string BrowserValue)[] _legacyBrowserFlags =
    {
        ("browserDefault", ToolIds.DefaultBrowser),
        ("browserChrome", ToolIds.ChromeBrowser),
        ("browserFirefox", ToolIds.FirefoxBrowser),
    };

    /// <summary>A legacy profile has no "tools" property.</summary>
    public static bool IsLegacyProfile(JsonObject profileNode) => !profileNode.ContainsKey("tools");

    public static ProjectProfile Convert(JsonObject legacyProfileNode)
    {
        var convertedProfile = new ProjectProfile
        {
            Name = ReadString(legacyProfileNode, "name") ?? ProjectProfile.DefaultProfileName,
            ProjectType = ReadFlag(legacyProfileNode, "isSymfony") ? ProjectType.Symfony : ProjectType.Other,
        };
        foreach (var (legacyPropertyName, toolId) in _legacyToolFlags)
            convertedProfile.Tools[toolId] = new ToolSelection { IsEnabled = ReadFlag(legacyProfileNode, legacyPropertyName) };
        var mercureScript = ReadString(legacyProfileNode, "mercureScript");
        if (mercureScript is not null)
            convertedProfile.Tools[ToolIds.Mercure].Options[ToolIds.MercureScriptOption] = new List<string> { mercureScript };
        convertedProfile.Tools[ToolIds.Browser].Options[ToolIds.BrowserTargetsOption] = _legacyBrowserFlags
            .Where(browserFlag => ReadFlag(legacyProfileNode, browserFlag.LegacyPropertyName))
            .Select(browserFlag => browserFlag.BrowserValue)
            .ToList();
        return convertedProfile;
    }

    private static bool ReadFlag(JsonObject profileNode, string propertyName)
        => profileNode.TryGetPropertyValue(propertyName, out var propertyNode)
           && propertyNode is JsonValue propertyValue
           && propertyValue.TryGetValue<bool>(out var flag)
           && flag;

    private static string? ReadString(JsonObject profileNode, string propertyName)
        => profileNode.TryGetPropertyValue(propertyName, out var propertyNode)
           && propertyNode is JsonValue propertyValue
           && propertyValue.TryGetValue<string>(out var text)
           && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;
}
