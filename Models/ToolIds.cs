namespace DevLauncher.Models;

/// <summary>Stable identifiers of the tools, used as keys in the saved profiles.</summary>
public static class ToolIds
{
    public const string VSCode = "vscode";
    public const string VisualStudio = "visual-studio";
    public const string SymfonyServer = "symfony-server";
    public const string Tailwind = "tailwind";
    public const string Mercure = "mercure";
    public const string Apache = "apache";
    public const string MySql = "mysql";
    public const string FileZilla = "filezilla";
    public const string XamppPanel = "xampp-panel";
    public const string Terminal = "terminal";
    public const string Browser = "browser";

    /// <summary>Option of the Mercure tool : script started from the Mercure folder.</summary>
    public const string MercureScriptOption = "script";

    /// <summary>Option of the browser tool : browsers opening the project URL.</summary>
    public const string BrowserTargetsOption = "targets";

    public const string DefaultBrowser = "default";
    public const string ChromeBrowser = "chrome";
    public const string FirefoxBrowser = "firefox";
}
