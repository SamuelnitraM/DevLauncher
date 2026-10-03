using System.IO;
using DevLauncher.Models;
using DevLauncher.Services.Hosting;

namespace DevLauncher.Services.Tools;

// The Stop methods of the service tools target the instances DevLauncher does not own : services run as VSCode tasks,
// or instances left by a previous session. Services run by DevLauncher are stopped with their process tree by their host.

/// <summary>
/// Local Symfony web server, started on the configured port. It prints its real address (http or https when its
/// certificate is installed), which the browser tool opens.
/// </summary>
public sealed class SymfonyServerTool : ServiceTool
{
    public override string Id => ToolIds.SymfonyServer;
    public override string DisplayName => "Symfony Server";
    public override string Icon => "🚀";
    public override ToolCategory Category => ToolCategories.ProjectServices;
    public override IReadOnlyCollection<ProjectType>? SupportedProjectTypes => new[] { ProjectType.Symfony };

    public override ServiceCommand? BuildServiceCommand(ToolExecutionContext context)
        => new(Id, DisplayName, context.ProjectPath, "symfony",
            new[] { "server:start", $"--port={AppSettings.SymfonyPort}" }, "symfony",
            AnnouncesApplicationUrl: true, ReadinessPattern: "listening");

    /// <summary>The port is checked only when the server is about to start : a server of this project already run by DevLauncher keeps it.</summary>
    public override IReadOnlyList<RequiredPort> GetRequiredPorts(ToolExecutionContext context)
        => context.ServiceHost.IsServiceRunning(context.ProjectPath, Id)
            ? Array.Empty<RequiredPort>()
            : new[] { new RequiredPort(AppSettings.SymfonyPort, Array.Empty<string>()) };

    public override async Task StopAsync(ToolExecutionContext context)
    {
        context.Log.Info($"⏹ Arrêt du serveur Symfony ({context.ProjectName})…");
        await context.ProcessLauncher.RunHiddenCommandAsync("symfony", new[] { "server:stop", "--dir", context.ProjectPath });
    }
}

/// <summary>Tailwind watcher of symfonycasts/tailwind-bundle.</summary>
public sealed class TailwindTool : ServiceTool
{
    public override string Id => ToolIds.Tailwind;
    public override string DisplayName => "Tailwind Watch";
    public override string Icon => "🎨";
    public override ToolCategory Category => ToolCategories.ProjectServices;
    public override IReadOnlyCollection<ProjectType>? SupportedProjectTypes => new[] { ProjectType.Symfony };

    public override ServiceCommand? BuildServiceCommand(ToolExecutionContext context)
        => new(Id, DisplayName, context.ProjectPath, "symfony",
            new[] { "console", "tailwind:build", "--watch" }, "symfony", ReadinessPattern: @"\bDone in\b");

    /// <summary>Stops the Tailwind binary downloaded inside this project only.</summary>
    public override async Task StopAsync(ToolExecutionContext context)
    {
        context.Log.Info($"⏹ Arrêt de Tailwind ({context.ProjectName})…");
        await context.ProcessLauncher.StopProcessesAsync("Tailwind", process =>
            process.ProcessName.StartsWith("tailwindcss", StringComparison.OrdinalIgnoreCase)
            && ProcessHelper.IsExecutableInside(process, context.ProjectPath));
    }
}

/// <summary>Mercure hub, started by one of the start*.ps1 scripts of the Mercure folder.</summary>
public sealed class MercureTool : ServiceTool
{
    public override string Id => ToolIds.Mercure;
    public override string DisplayName => "Mercure";
    public override string Icon => "📡";
    public override ToolCategory Category => ToolCategories.ProjectServices;
    public override IReadOnlyCollection<ProjectType>? SupportedProjectTypes => new[] { ProjectType.Symfony };

    public override IReadOnlyList<ToolOptionDefinition> Options { get; } = new[]
    {
        new ToolOptionDefinition(ToolIds.MercureScriptOption, "Script Mercure :", ToolOptionKind.SingleChoice,
            _ => GetMercureScripts(), _ => Array.Empty<string>()),
    };

    public override ServiceCommand? BuildServiceCommand(ToolExecutionContext context)
    {
        var mercureScriptName = context.GetOptionValues(ToolIds.MercureScriptOption).FirstOrDefault();
        var mercureScriptPath = mercureScriptName is null ? null : Path.Combine(AppSettings.MercureDir, mercureScriptName);
        if (mercureScriptPath is null || !File.Exists(mercureScriptPath))
        {
            context.Log.Error($"❌ Script Mercure introuvable dans {AppSettings.MercureDir} — Mercure ne sera pas lancé.");
            return null;
        }
        return new ServiceCommand(Id, DisplayName, AppSettings.MercureDir, "powershell",
            new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", mercureScriptPath }, "mercure");
    }

    public override async Task StopAsync(ToolExecutionContext context)
    {
        context.Log.Info("⏹ Arrêt de Mercure…");
        await context.ProcessLauncher.StopProcessesAsync("Mercure", process => process.ProcessName.Equals("mercure", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<ToolOptionChoice> GetMercureScripts()
    {
        try
        {
            return Directory.Exists(AppSettings.MercureDir)
                ? Directory.GetFiles(AppSettings.MercureDir, "start*.ps1")
                    .Select(Path.GetFileName)
                    .OfType<string>()
                    .Select(scriptName => new ToolOptionChoice(scriptName, scriptName))
                    .ToList()
                : Array.Empty<ToolOptionChoice>();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<ToolOptionChoice>();
        }
    }
}

/// <summary>Symfony Messenger worker consuming the transports of the project (messenger:consume).</summary>
public sealed class MessengerWorkerTool : ServiceTool
{
    private const string DefaultTransports = "async";

    public override string Id => ToolIds.MessengerWorker;
    public override string DisplayName => "Messenger (worker)";
    public override string Icon => "📨";
    public override ToolCategory Category => ToolCategories.ProjectServices;
    public override IReadOnlyCollection<ProjectType>? SupportedProjectTypes => new[] { ProjectType.Symfony };

    public override IReadOnlyList<ToolOptionDefinition> Options { get; } = new[]
    {
        new ToolOptionDefinition(ToolIds.MessengerTransportsOption, "Transports (séparés par des espaces) :", ToolOptionKind.Text,
            _ => Array.Empty<ToolOptionChoice>(), _ => new[] { DefaultTransports }),
    };

    public override ServiceCommand? BuildServiceCommand(ToolExecutionContext context)
    {
        var transportNames = (context.GetOptionValues(ToolIds.MessengerTransportsOption).FirstOrDefault() ?? DefaultTransports)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (transportNames.Length == 0) transportNames = new[] { DefaultTransports };
        return new ServiceCommand(Id, DisplayName, context.ProjectPath, AppSettings.PhpExecutable,
            new[] { "bin/console", "messenger:consume" }.Concat(transportNames).Append("-vv").ToList(), "php");
    }
}

/// <summary>Mailpit : catches the mails sent by the project and shows them on http://localhost:8025.</summary>
public sealed class MailpitTool : ServiceTool
{
    public const string WebInterfaceUrl = "http://localhost:8025";

    public override string Id => ToolIds.Mailpit;
    public override string DisplayName => "Mailpit (mails de test)";
    public override string Icon => "📮";
    public override ToolCategory Category => ToolCategories.ProjectServices;

    public override IReadOnlyList<RequiredPort> GetRequiredPorts(ToolExecutionContext context)
        => context.ServiceHost.IsServiceRunning(context.ProjectPath, Id)
            ? Array.Empty<RequiredPort>()
            : new[] { new RequiredPort(8025, new[] { "mailpit" }), new RequiredPort(1025, new[] { "mailpit" }) };

    /// <summary>Offered only when Mailpit is installed.</summary>
    public override bool IsEnabledInSettings => ExecutableLocator.FindInPath("mailpit") is not null;

    public override ServiceCommand? BuildServiceCommand(ToolExecutionContext context)
    {
        context.Log.Info($"📮 Mailpit : SMTP sur localhost:1025, interface sur {WebInterfaceUrl}");
        return new ServiceCommand(Id, DisplayName, context.ProjectPath, "mailpit", Array.Empty<string>(), "mailpit", ReadinessPattern: "accessible via");
    }

    public override async Task StopAsync(ToolExecutionContext context)
    {
        context.Log.Info("⏹ Arrêt de Mailpit…");
        await context.ProcessLauncher.StopProcessesAsync("Mailpit", process => process.ProcessName.Equals("mailpit", StringComparison.OrdinalIgnoreCase));
    }
}
