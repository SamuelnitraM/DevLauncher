using System.IO;
using DevLauncher.Models;
using DevLauncher.Services.Stacks;

namespace DevLauncher.Services.Tools;

/// <summary>
/// Opens an interactive terminal in the project folder : a tab of the DevLauncher window of Windows Terminal,
/// or a console window of the chosen shell when Windows Terminal is not installed.
/// </summary>
public sealed class TerminalTool : LaunchTool
{
    private const string DefaultShell = "default";
    private const string WindowsPowerShell = "powershell";
    private const string PowerShellCore = "pwsh";
    private const string CommandPrompt = "cmd";
    private const string GitBash = "git-bash";

    public override string Id => ToolIds.Terminal;
    public override string DisplayName => "Ouvrir un terminal";
    public override string Icon => "🖥️";
    public override ToolCategory Category => ToolCategories.Utilities;
    public override LaunchStage Stage => LaunchStage.Workspace;

    public override IReadOnlyList<ToolOptionDefinition> Options { get; } = new[]
    {
        new ToolOptionDefinition(ToolIds.TerminalShellOption, "Shell :", ToolOptionKind.SingleChoice,
            _ => GetShellChoices(), _ => new[] { DefaultShell }),
    };

    public override Task<ToolStartResult> StartAsync(ToolExecutionContext context)
    {
        var shellChoice = context.GetOptionValues(ToolIds.TerminalShellOption).FirstOrDefault() ?? DefaultShell;
        context.Log.Info("🖥️ Ouverture d'un terminal…");
        var isStarted = TerminalTabLauncher.OpenTab(context, context.ProjectName, GetShellCommandLine(shellChoice));
        if (isStarted) context.Log.Info("   ✅ Terminal ouvert");
        return Task.FromResult(isStarted ? ToolStartResult.Started : ToolStartResult.Failed);
    }

    /// <summary>Shells offered : the default profile of Windows Terminal, and the shells installed on this computer.</summary>
    private static IReadOnlyList<ToolOptionChoice> GetShellChoices()
    {
        var shellChoices = new List<ToolOptionChoice>
        {
            new(DefaultShell, "Profil par défaut de Windows Terminal"),
            new(WindowsPowerShell, "Windows PowerShell"),
        };
        if (ExecutableLocator.FindInPath("pwsh") is not null) shellChoices.Add(new ToolOptionChoice(PowerShellCore, "PowerShell 7"));
        shellChoices.Add(new ToolOptionChoice(CommandPrompt, "Invite de commandes"));
        if (FindGitBash() is not null) shellChoices.Add(new ToolOptionChoice(GitBash, "Git Bash"));
        return shellChoices;
    }

    /// <summary>Command line of the shell, empty for the default profile of Windows Terminal.</summary>
    private static IReadOnlyList<string> GetShellCommandLine(string shellChoice) => shellChoice switch
    {
        WindowsPowerShell => new[] { "powershell.exe", "-NoLogo" },
        PowerShellCore => new[] { "pwsh.exe", "-NoLogo" },
        CommandPrompt => new[] { "cmd.exe" },
        GitBash when FindGitBash() is { } gitBashPath => new[] { gitBashPath, "--login", "-i" },
        _ => Array.Empty<string>(),
    };

    private static string? FindGitBash()
        => new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(Environment.GetFolderPath)
            .Where(programFilesDirectory => !string.IsNullOrEmpty(programFilesDirectory))
            .Select(programFilesDirectory => Path.Combine(programFilesDirectory, "Git", "bin", "bash.exe"))
            .FirstOrDefault(File.Exists);
}

/// <summary>
/// Opens a tab of the DevLauncher window of Windows Terminal in the project folder, running a command line or the default
/// profile ; a console window when Windows Terminal is not installed.
/// </summary>
public static class TerminalTabLauncher
{
    /// <param name="commandLine">Program and arguments run in the tab, empty for the default profile of Windows Terminal.</param>
    public static bool OpenTab(ToolExecutionContext context, string tabTitle, IReadOnlyList<string> commandLine)
    {
        if (IsWindowsTerminalInstalled())
        {
            var windowsTerminalArguments = new List<string>
            {
                "-w", ProcessLauncher.WindowsTerminalWindowName, "new-tab", "--title", tabTitle,
                "--suppressApplicationTitle", "-d", context.ProjectPath,
            };
            windowsTerminalArguments.AddRange(commandLine);
            return context.ProcessLauncher.StartWindowsTerminal(windowsTerminalArguments, context.RunsUnelevated);
        }
        var consoleCommandLine = commandLine.Count > 0 ? commandLine : new[] { "powershell.exe" };
        context.Log.Info("   ℹ️ Windows Terminal absent : ouverture d'une console");
        return context.ProcessLauncher.StartConsole(consoleCommandLine[0], consoleCommandLine.Skip(1).ToList(), context.ProjectPath, context.RunsUnelevated);
    }

    /// <summary>Windows Terminal is reached through its execution alias, in the PATH or in the WindowsApps folder of the user.</summary>
    public static bool IsWindowsTerminalInstalled()
        => ExecutableLocator.FindInPath("wt") is not null
           || File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe"));
}

/// <summary>Opens the project URL in the selected browsers once its web server answers.</summary>
public sealed class BrowserTool : LaunchTool
{
    private static readonly TimeSpan ServerReadinessTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan AnnouncedSymfonyUrlTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan AssetBuildReadinessTimeout = TimeSpan.FromSeconds(20);

    public override string Id => ToolIds.Browser;
    public override string DisplayName => "Ouvrir le navigateur";
    public override string Icon => "🌍";
    public override ToolCategory Category => ToolCategories.Utilities;
    public override LaunchStage Stage => LaunchStage.Finalization;

    public override IReadOnlyList<ToolOptionDefinition> Options { get; } = new[]
    {
        new ToolOptionDefinition(ToolIds.BrowserTargetsOption, "Navigateurs :", ToolOptionKind.MultipleChoice,
            _ => new[]
            {
                new ToolOptionChoice(ToolIds.DefaultBrowser, "🌐 Défaut"),
                new ToolOptionChoice(ToolIds.ChromeBrowser, "🟡 Google Chrome"),
                new ToolOptionChoice(ToolIds.FirefoxBrowser, "🦊 Firefox"),
            },
            _ => new[] { ToolIds.DefaultBrowser }),
    };

    public override async Task<ToolStartResult> StartAsync(ToolExecutionContext context)
    {
        var projectUrl = context.Profile.ProjectType switch
        {
            ProjectType.Symfony => await WaitForSymfonyAsync(context),
            ProjectType.Other or ProjectType.WordPress => await WaitForApacheAsync(context),
            _ => await WaitForAnnouncedUrlAsync(context),
        };
        if (projectUrl is null) return ToolStartResult.Failed;
        await WaitForProjectServicesAsync(context);
        context.Log.Info($"🌍 Ouverture du navigateur : {projectUrl}");
        return OpenInBrowsers(context, projectUrl) ? ToolStartResult.Started : ToolStartResult.Failed;
    }

    /// <summary>True when the service of this tool runs in DevLauncher for this launch, so that its output can be read.</summary>
    private static bool IsServiceReadByDevLauncher(ToolExecutionContext context, string serviceToolId)
        => context.Profile.IsToolEnabled(serviceToolId) && !(AppSettings.HostServicesInVSCode && context.Profile.IsToolEnabled(ToolIds.VSCode));

    /// <summary>
    /// Uses the address printed by the Symfony server (real scheme and port), and falls back on the configured port
    /// when the server runs elsewhere or announces nothing.
    /// </summary>
    private static async Task<string?> WaitForSymfonyAsync(ToolExecutionContext context)
    {
        var configuredUrl = $"http://127.0.0.1:{AppSettings.SymfonyPort}";
        if (!IsServiceReadByDevLauncher(context, ToolIds.SymfonyServer))
            return await WaitForPortServerAsync(context, "Symfony Server", AppSettings.SymfonyPort, configuredUrl, context.Profile.IsToolEnabled(ToolIds.SymfonyServer));
        context.Log.Info("⏳ Attente de l'adresse annoncée par le serveur Symfony…");
        var announcedUrl = await context.ServiceHost.WaitForAnnouncedUrlAsync(context.ProjectPath, AnnouncedSymfonyUrlTimeout);
        if (announcedUrl is not null)
        {
            context.Log.Info($"✅ Serveur Symfony prêt : {announcedUrl}");
            return announcedUrl;
        }
        context.Log.Info("ℹ️ Aucune adresse annoncée par le serveur Symfony : test du port configuré");
        return await WaitForPortServerAsync(context, "Symfony Server", AppSettings.SymfonyPort, configuredUrl, isServerStartedByThisLaunch: true);
    }

    /// <summary>Lets the asset builders and watchers of the project finish their first build, so that the page opens styled.</summary>
    private static async Task WaitForProjectServicesAsync(ToolExecutionContext context)
    {
        var notReadyServiceTitles = await context.ServiceHost.WaitForProjectServicesReadyAsync(context.ProjectPath, AssetBuildReadinessTimeout);
        if (notReadyServiceTitles.Count > 0)
            context.Log.Info($"ℹ️ Pas encore prêt(s) après {AssetBuildReadinessTimeout.TotalSeconds:0}s : {string.Join(", ", notReadyServiceTitles)} — ouverture quand même");
    }

    private static async Task<string?> WaitForApacheAsync(ToolExecutionContext context)
    {
        var apacheUrl = ProjectUrlResolver.GetApacheUrl(context.ProjectPath, new VirtualHostService().FindHostName(context.ProjectPath));
        if (apacheUrl is null)
        {
            context.Log.Error($"❌ Le projet n'est pas dans {AppSettings.ApacheDocumentRoot} : Apache ne le sert pas (crée-lui un hôte virtuel : clic droit sur le projet), la page ne peut pas s'ouvrir.");
            return null;
        }
        return await WaitForPortServerAsync(context, "Apache", AppSettings.LocalWebPort, apacheUrl, context.Profile.IsToolEnabled(ToolIds.Apache));
    }

    /// <summary>Waits for a server listening on a known port, when this launch starts it.</summary>
    private static async Task<string?> WaitForPortServerAsync(ToolExecutionContext context, string serverName, int serverPort, string projectUrl, bool isServerStartedByThisLaunch)
    {
        if (await PortProbe.IsPortOpenAsync(serverPort)) return projectUrl;
        if (!isServerStartedByThisLaunch)
        {
            context.Log.Error($"❌ {serverName} n'est ni actif ni sélectionné : la page {projectUrl} ne peut pas s'ouvrir.");
            return null;
        }
        context.Log.Info($"⏳ Attente de {serverName} sur le port {serverPort}…");
        if (await PortProbe.WaitForPortAsync(serverPort, ServerReadinessTimeout))
        {
            context.Log.Info($"✅ {serverName} est prêt");
            return projectUrl;
        }
        context.Log.Error($"❌ {serverName} ne répond pas après {ServerReadinessTimeout.TotalSeconds:0}s : la page {projectUrl} ne peut pas s'ouvrir.");
        return null;
    }

    /// <summary>Waits for the URL printed by the development server of the project, read from its output.</summary>
    private static async Task<string?> WaitForAnnouncedUrlAsync(ToolExecutionContext context)
    {
        var webServerToolId = context.Profile.ProjectType switch
        {
            ProjectType.Laravel => ToolIds.LaravelServer,
            ProjectType.Node => ToolIds.NpmScript,
            ProjectType.Django => ToolIds.DjangoServer,
            _ => ToolIds.DotNetWatch,
        };
        var projectTypeName = ProjectTypeLabels.GetName(context.Profile.ProjectType);
        if (!context.Profile.IsToolEnabled(webServerToolId))
        {
            context.Log.Error($"❌ Aucun serveur de développement {projectTypeName} sélectionné : l'adresse du projet est inconnue.");
            return null;
        }
        if (AppSettings.HostServicesInVSCode && context.Profile.IsToolEnabled(ToolIds.VSCode))
        {
            context.Log.Error("❌ Les services tournent dans VSCode : DevLauncher ne lit pas leur sortie et ne connaît pas l'adresse du projet.");
            return null;
        }
        context.Log.Info($"⏳ Attente de l'adresse annoncée par le serveur {projectTypeName}…");
        var announcedUrl = await context.ServiceHost.WaitForAnnouncedUrlAsync(context.ProjectPath, ServerReadinessTimeout);
        if (announcedUrl is null)
            context.Log.Error($"❌ Le serveur {projectTypeName} n'a annoncé aucune adresse après {ServerReadinessTimeout.TotalSeconds:0}s : voir son onglet.");
        return announcedUrl;
    }

    private static bool OpenInBrowsers(ToolExecutionContext context, string url)
    {
        var browserTargets = context.GetOptionValues(ToolIds.BrowserTargetsOption);
        if (browserTargets.Count == 0)
        {
            context.Log.Error("❌ Aucun navigateur coché");
            return false;
        }
        var isAnyBrowserStarted = false;
        foreach (var browserTarget in browserTargets)
        {
            isAnyBrowserStarted |= browserTarget switch
            {
                ToolIds.DefaultBrowser => context.ProcessLauncher.StartShellProcess(url, null, context.RunsUnelevated),
                ToolIds.ChromeBrowser => StartBrowser(context, "Chrome", AppSettings.ChromeExe, url),
                ToolIds.FirefoxBrowser => StartBrowser(context, "Firefox", AppSettings.FirefoxExe, url),
                _ => false,
            };
        }
        return isAnyBrowserStarted;
    }

    private static bool StartBrowser(ToolExecutionContext context, string browserName, string browserExecutablePath, string url)
    {
        if (File.Exists(browserExecutablePath)) return context.ProcessLauncher.StartShellProcess(browserExecutablePath, url, context.RunsUnelevated);
        context.Log.Error($"❌ {browserName} introuvable : {browserExecutablePath}");
        return false;
    }
}
