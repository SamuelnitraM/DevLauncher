using System.IO;
using DevLauncher.Models;
using DevLauncher.Services.Assistants;

namespace DevLauncher;

/// <summary>
/// Global application settings. Values below are the defaults, overridden by settings.json at startup.
/// </summary>
public static class AppSettings
{
    /// <summary>Folders whose sub-folders are projects</summary>
    public static List<string> ProjectRoots { get; set; } = new() { @"C:\xampp\htdocs" };

    /// <summary>Projects added one by one, outside the project roots</summary>
    public static List<string> ExtraProjectPaths { get; set; } = new();

    /// <summary>Folder names never listed as projects (XAMPP default pages by default)</summary>
    public static List<string> ExcludedFolderNames { get; set; } = new() { "dashboard", "img", "webalizer", "xampp", "forbidden", "restricted" };

    /// <summary>Root of the XAMPP installation</summary>
    public static string XamppDir { get; set; } = @"C:\xampp";

    /// <summary>Folder served by Apache at http://localhost/</summary>
    public static string ApacheDocumentRoot => Path.Combine(XamppDir, "htdocs");

    /// <summary>PHP executable : the one of XAMPP when present, else the one of the PATH</summary>
    public static string PhpExecutable
    {
        get
        {
            var xamppPhpExecutable = Path.Combine(XamppDir, "php", "php.exe");
            return File.Exists(xamppPhpExecutable) ? xamppPhpExecutable : "php";
        }
    }

    // XAMPP executables
    public static string ApacheExe { get; set; } = @"C:\xampp\apache\bin\httpd.exe";
    public static string MySQLExe { get; set; } = @"C:\xampp\mysql\bin\mysqld.exe";
    public static string FileZillaExe { get; set; } = @"C:\xampp\FileZillaFTP\FileZillaServer.exe";
    public static string XamppPanel { get; set; } = @"C:\xampp\xampp-control.exe";

    // MySQL configuration
    public static string MySQLConfig { get; set; } = @"C:\xampp\mysql\bin\my.ini";

    // Browsers
    public static string ChromeExe { get; set; } = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
    public static string FirefoxExe { get; set; } = @"D:\Firefox\firefox.exe";

    /// <summary>Folder containing the Mercure scripts (start*.ps1)</summary>
    public static string MercureDir { get; set; } = @"C:\mercure";

    /// <summary>VSCode launcher : command available in the PATH (code) or absolute path to Code.exe</summary>
    public static string VSCodeExecutable { get; set; } = "code";

    /// <summary>Path to the Visual Studio executable</summary>
    public static string VisualStudioExecutable { get; set; } = @"C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\devenv.exe";

    /// <summary>Local port of the Symfony server, passed to symfony server:start and used for the browser URL</summary>
    public static int SymfonyPort { get; set; } = 8000;

    /// <summary>Local port of Apache, used for the browser URL of non-Symfony projects</summary>
    public static int LocalWebPort { get; set; } = 80;

    /// <summary>Runs the services as VSCode tasks when VSCode is launched, instead of running them in DevLauncher</summary>
    public static bool HostServicesInVSCode { get; set; }

    /// <summary>Shows the detail messages in the launch log and saves them, with the output of the services, in the persistent log</summary>
    public static bool DetailedLogging { get; set; }

    /// <summary>Minimizing the main window hides it in the notification area</summary>
    public static bool MinimizeToTray { get; set; } = true;

    /// <summary>Windows notifications : environment ready, service stopped unexpectedly</summary>
    public static bool ShowNotifications { get; set; } = true;

    /// <summary>Shortcut bringing DevLauncher forward with its command palette from any application, empty when disabled</summary>
    public static string GlobalHotkey { get; set; } = "Ctrl+Alt+D";

    /// <summary>Color theme : system (follows Windows), light or dark</summary>
    public static string Theme { get; set; } = ThemeNames.System;

    /// <summary>Settings of the AI assistants, one entry per assistant of the catalog</summary>
    public static List<AssistantSettings> Assistants { get; set; } = AssistantCatalog.MergeWithDefaults(null);
}

/// <summary>Values of the theme setting.</summary>
public static class ThemeNames
{
    public const string System = "system";
    public const string Light = "light";
    public const string Dark = "dark";
}
