namespace DevLauncher.Models;

/// <summary>Ways of opening a chat assistant.</summary>
public static class AssistantModes
{
    public const string Browser = "browser";
    public const string Application = "application";
}

/// <summary>Project of an assistant, opened in the browser from its URL.</summary>
public class AssistantProject
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

/// <summary>User settings of an assistant.</summary>
public class AssistantSettings
{
    public string Id { get; set; } = string.Empty;

    /// <summary>An enabled assistant appears in the tools of the main window.</summary>
    public bool IsEnabled { get; set; }

    public string DefaultMode { get; set; } = AssistantModes.Browser;

    /// <summary>Page opened for a new conversation.</summary>
    public string WebUrl { get; set; } = string.Empty;

    /// <summary>Desktop application : executable, shortcut, protocol URI or shell:AppsFolder\… target. Empty means detected.</summary>
    public string ApplicationTarget { get; set; } = string.Empty;

    public List<AssistantProject> Projects { get; set; } = new();
}
