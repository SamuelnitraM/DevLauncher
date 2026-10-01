using System.IO;
using DevLauncher.Models;

namespace DevLauncher.Services.Tools;

/// <summary>Opens an interactive terminal tab in the project folder.</summary>
public sealed class TerminalTool : LaunchTool
{
    public override string Id => ToolIds.Terminal;
    public override string DisplayName => "Ouvrir un terminal";
    public override string Icon => "🖥️";
    public override ToolCategory Category => ToolCategories.Utilities;
    public override LaunchStage Stage => LaunchStage.Workspace;

    public override Task<ToolStartResult> StartAsync(ToolExecutionContext context)
    {
        context.Log.Info("🖥️ Ouverture d'un terminal…");
        var isStarted = context.ProcessLauncher.StartWindowsTerminal(new[]
        {
            "-w", ProcessLauncher.WindowsTerminalWindowName, "new-tab", "--title", context.ProjectName,
            "--suppressApplicationTitle", "-d", context.ProjectPath,
        });
        if (isStarted) context.Log.Info("   ✅ Terminal ouvert");
        return Task.FromResult(isStarted ? ToolStartResult.Started : ToolStartResult.Failed);
    }
}

/// <summary>Opens the project URL in the selected browsers once its web server answers.</summary>
public sealed class BrowserTool : LaunchTool
{
    private static readonly TimeSpan ServerReadinessTimeout = TimeSpan.FromSeconds(45);

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
            ProjectType.Symfony => await WaitForPortServerAsync(context, "Symfony Server", AppSettings.SymfonyPort,
                $"http://127.0.0.1:{AppSettings.SymfonyPort}", context.Profile.IsToolEnabled(ToolIds.SymfonyServer)),
            ProjectType.Other or ProjectType.WordPress => await WaitForApacheAsync(context),
            _ => await WaitForAnnouncedUrlAsync(context),
        };
        if (projectUrl is null) return ToolStartResult.Failed;
        context.Log.Info($"🌍 Ouverture du navigateur : {projectUrl}");
        return OpenInBrowsers(context, projectUrl) ? ToolStartResult.Started : ToolStartResult.Failed;
    }

    /// <summary>
    /// Returns the URL of a project served by Apache : its path relative to the Apache document root.
    /// Returns null for a project located outside of it.
    /// </summary>
    public static string? BuildApacheUrl(string projectPath, string apacheDocumentRoot, int apachePort)
    {
        var relativeProjectPath = Path.GetRelativePath(Path.GetFullPath(apacheDocumentRoot), Path.GetFullPath(projectPath));
        if (relativeProjectPath.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativeProjectPath)) return null;
        var urlPath = string.Join('/', relativeProjectPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Select(Uri.EscapeDataString));
        return $"http://localhost{(apachePort == 80 ? string.Empty : $":{apachePort}")}/{urlPath}/";
    }

    private static async Task<string?> WaitForApacheAsync(ToolExecutionContext context)
    {
        var apacheUrl = BuildApacheUrl(context.ProjectPath, AppSettings.ApacheDocumentRoot, AppSettings.LocalWebPort);
        if (apacheUrl is null)
        {
            context.Log.Error($"❌ Le projet n'est pas dans {AppSettings.ApacheDocumentRoot} : Apache ne le sert pas, la page ne peut pas s'ouvrir.");
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
                ToolIds.DefaultBrowser => context.ProcessLauncher.StartShellProcess(url),
                ToolIds.ChromeBrowser => StartBrowser(context, "Chrome", AppSettings.ChromeExe, url),
                ToolIds.FirefoxBrowser => StartBrowser(context, "Firefox", AppSettings.FirefoxExe, url),
                _ => false,
            };
        }
        return isAnyBrowserStarted;
    }

    private static bool StartBrowser(ToolExecutionContext context, string browserName, string browserExecutablePath, string url)
    {
        if (File.Exists(browserExecutablePath)) return context.ProcessLauncher.StartShellProcess(browserExecutablePath, url);
        context.Log.Error($"❌ {browserName} introuvable : {browserExecutablePath}");
        return false;
    }
}
