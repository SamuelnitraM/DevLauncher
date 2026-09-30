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
            () => new[]
            {
                new ToolOptionChoice(ToolIds.DefaultBrowser, "🌐 Défaut"),
                new ToolOptionChoice(ToolIds.ChromeBrowser, "🟡 Google Chrome"),
                new ToolOptionChoice(ToolIds.FirefoxBrowser, "🦊 Firefox"),
            },
            new[] { ToolIds.DefaultBrowser }),
    };

    public override async Task<ToolStartResult> StartAsync(ToolExecutionContext context)
    {
        var isSymfonyProject = context.Profile.ProjectType == ProjectType.Symfony;
        var serverName = isSymfonyProject ? "Symfony Server" : "Apache";
        var serverPort = isSymfonyProject ? AppSettings.SymfonyPort : AppSettings.LocalWebPort;
        var isServerStartedByThisLaunch = context.Profile.IsToolEnabled(isSymfonyProject ? ToolIds.SymfonyServer : ToolIds.Apache);
        var projectUrl = isSymfonyProject
            ? $"http://127.0.0.1:{serverPort}"
            : $"http://localhost{(serverPort == 80 ? string.Empty : $":{serverPort}")}/{Uri.EscapeDataString(context.ProjectName)}/";
        if (!await PortProbe.IsPortOpenAsync(serverPort))
        {
            if (!isServerStartedByThisLaunch)
            {
                context.Log.Error($"❌ {serverName} n'est ni actif ni sélectionné : la page {projectUrl} ne peut pas s'ouvrir.");
                return ToolStartResult.Failed;
            }
            context.Log.Info($"⏳ Attente de {serverName} sur le port {serverPort}…");
            if (!await PortProbe.WaitForPortAsync(serverPort, ServerReadinessTimeout))
            {
                context.Log.Error($"❌ {serverName} ne répond pas après {ServerReadinessTimeout.TotalSeconds:0}s : la page {projectUrl} ne peut pas s'ouvrir.");
                return ToolStartResult.Failed;
            }
            context.Log.Info($"✅ {serverName} est prêt");
        }
        context.Log.Info($"🌍 Ouverture du navigateur : {projectUrl}");
        return OpenInBrowsers(context, projectUrl) ? ToolStartResult.Started : ToolStartResult.Failed;
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
