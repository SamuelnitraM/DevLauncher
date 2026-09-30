using System.IO;
using DevLauncher.Models;
using DevLauncher.Services.Hosting;

namespace DevLauncher.Services.Tools;

// The Stop methods of the service tools are used when the services run as VSCode tasks :
// services run by DevLauncher are stopped with their whole process tree by the service process host.

/// <summary>Local Symfony web server, started on the configured port.</summary>
public sealed class SymfonyServerTool : ServiceTool
{
    public override string Id => ToolIds.SymfonyServer;
    public override string DisplayName => "Symfony Server";
    public override string Icon => "🚀";
    public override ToolCategory Category => ToolCategories.Symfony;
    public override IReadOnlyCollection<ProjectType>? SupportedProjectTypes => new[] { ProjectType.Symfony };

    public override ServiceCommand? BuildServiceCommand(ToolExecutionContext context)
        => new(Id, DisplayName, context.ProjectPath, "symfony",
            new[] { "server:start", $"--port={AppSettings.SymfonyPort}" }, "symfony");

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
    public override ToolCategory Category => ToolCategories.Symfony;
    public override IReadOnlyCollection<ProjectType>? SupportedProjectTypes => new[] { ProjectType.Symfony };

    public override ServiceCommand? BuildServiceCommand(ToolExecutionContext context)
        => new(Id, DisplayName, context.ProjectPath, "symfony",
            new[] { "console", "tailwind:build", "--watch" }, "symfony");

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
    public override ToolCategory Category => ToolCategories.Symfony;
    public override IReadOnlyCollection<ProjectType>? SupportedProjectTypes => new[] { ProjectType.Symfony };

    public override IReadOnlyList<ToolOptionDefinition> Options { get; } = new[]
    {
        new ToolOptionDefinition(ToolIds.MercureScriptOption, "Script Mercure :", ToolOptionKind.SingleChoice, GetMercureScripts, Array.Empty<string>()),
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
