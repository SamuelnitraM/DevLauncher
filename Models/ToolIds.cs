namespace DevLauncher.Models;

/// <summary>Stable identifiers of the tools, used as keys in the saved profiles.</summary>
public static class ToolIds
{
    public const string VSCode = "vscode";
    public const string VisualStudio = "visual-studio";
    public const string SymfonyServer = "symfony-server";
    public const string Tailwind = "tailwind";
    public const string Mercure = "mercure";
    public const string LaravelServer = "laravel-serve";
    public const string LaravelQueue = "laravel-queue";
    public const string NpmScript = "npm-script";
    public const string DjangoServer = "django-runserver";
    public const string DotNetWatch = "dotnet-watch";
    public const string Apache = "apache";
    public const string MySql = "mysql";
    public const string FileZilla = "filezilla";
    public const string XamppPanel = "xampp-panel";
    public const string Terminal = "terminal";
    public const string PreLaunchCommands = "pre-launch";
    public const string MessengerWorker = "messenger-worker";
    public const string Mailpit = "mailpit";
    public const string Browser = "browser";

    /// <summary>Tool of a chat assistant of the assistant catalog.</summary>
    public static string ChatAssistant(string assistantId) => $"assistant-{assistantId}";

    /// <summary>Option of the npm tool : script of package.json to run.</summary>
    public const string NpmScriptOption = "script";

    /// <summary>Option of the Mercure tool : script started from the Mercure folder.</summary>
    public const string MercureScriptOption = "script";

    /// <summary>Options of the pre-launch tool : predefined commands, and a command typed by the user.</summary>
    public const string PreLaunchCommandsOption = "commands";
    public const string PreLaunchCustomCommandOption = "custom";

    /// <summary>Option of the Messenger tool : transports consumed.</summary>
    public const string MessengerTransportsOption = "transports";

    /// <summary>Option of the terminal tool : shell opened in the tab.</summary>
    public const string TerminalShellOption = "shell";

    /// <summary>Option of the browser tool : browsers opening the project URL.</summary>
    public const string BrowserTargetsOption = "targets";

    /// <summary>Options of a chat assistant : opening mode, and project opened in the browser.</summary>
    public const string AssistantModeOption = "mode";
    public const string AssistantProjectOption = "project";

    public const string DefaultBrowser = "default";
    public const string ChromeBrowser = "chrome";
    public const string FirefoxBrowser = "firefox";
}
