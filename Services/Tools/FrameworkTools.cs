using System.IO;
using DevLauncher.Models;
using DevLauncher.Services.Hosting;

namespace DevLauncher.Services.Tools;

// Development servers and workers of the frameworks other than Symfony. Their web servers announce
// the project URL in their output, which the browser tool waits for.

/// <summary>Laravel development server (php artisan serve).</summary>
public sealed class LaravelServerTool : ServiceTool
{
    public override string Id => ToolIds.LaravelServer;
    public override string DisplayName => "Laravel Serve";
    public override string Icon => "🔺";
    public override ToolCategory Category => ToolCategories.ProjectServices;
    public override IReadOnlyCollection<ProjectType>? SupportedProjectTypes => new[] { ProjectType.Laravel };

    public override ServiceCommand? BuildServiceCommand(ToolExecutionContext context)
        => new(Id, DisplayName, context.ProjectPath, AppSettings.PhpExecutable, new[] { "artisan", "serve" }, "php", AnnouncesApplicationUrl: true);
}

/// <summary>Laravel queue worker (php artisan queue:work).</summary>
public sealed class LaravelQueueTool : ServiceTool
{
    public override string Id => ToolIds.LaravelQueue;
    public override string DisplayName => "Laravel Queue";
    public override string Icon => "📬";
    public override ToolCategory Category => ToolCategories.ProjectServices;
    public override IReadOnlyCollection<ProjectType>? SupportedProjectTypes => new[] { ProjectType.Laravel };

    public override ServiceCommand? BuildServiceCommand(ToolExecutionContext context)
        => new(Id, DisplayName, context.ProjectPath, AppSettings.PhpExecutable, new[] { "artisan", "queue:work" }, "php");
}

/// <summary>
/// Script of package.json (npm run dev, watch…). For a Node.js project it is the web server of the project ;
/// for a PHP framework it builds the assets (Vite, Webpack Encore).
/// </summary>
public sealed class NpmScriptTool : ServiceTool
{
    private const string PreferredScript = "dev";

    public override string Id => ToolIds.NpmScript;
    public override string DisplayName => "Script npm";
    public override string Icon => "📦";
    public override ToolCategory Category => ToolCategories.ProjectServices;
    public override IReadOnlyCollection<ProjectType>? SupportedProjectTypes => new[] { ProjectType.Node, ProjectType.Laravel, ProjectType.Symfony };

    public override IReadOnlyList<ToolOptionDefinition> Options { get; } = new[]
    {
        new ToolOptionDefinition(ToolIds.NpmScriptOption, "Script :", ToolOptionKind.SingleChoice,
            optionContext => optionContext.ProjectPath is null
                ? Array.Empty<ToolOptionChoice>()
                : ProjectScanner.GetNpmScripts(optionContext.ProjectPath).Select(script => new ToolOptionChoice(script, $"npm run {script}")).ToList(),
            _ => new[] { PreferredScript }),
    };

    public override ServiceCommand? BuildServiceCommand(ToolExecutionContext context)
    {
        var scriptName = context.GetOptionValues(ToolIds.NpmScriptOption).FirstOrDefault();
        if (string.IsNullOrEmpty(scriptName) || !ProjectScanner.GetNpmScripts(context.ProjectPath).Contains(scriptName))
        {
            context.Log.Error("❌ Aucun script npm choisi ou script absent de package.json — le script npm ne sera pas lancé.");
            return null;
        }
        // npm is a command script (npm.cmd) : it goes through cmd.exe, which the process tree stop also covers.
        return new ServiceCommand(Id, $"npm run {scriptName}", context.ProjectPath, "cmd.exe", new[] { "/c", "npm", "run", scriptName }, "node",
            AnnouncesApplicationUrl: context.Profile.ProjectType == ProjectType.Node);
    }
}

/// <summary>Django development server (python manage.py runserver).</summary>
public sealed class DjangoServerTool : ServiceTool
{
    public override string Id => ToolIds.DjangoServer;
    public override string DisplayName => "Django runserver";
    public override string Icon => "🐍";
    public override ToolCategory Category => ToolCategories.ProjectServices;
    public override IReadOnlyCollection<ProjectType>? SupportedProjectTypes => new[] { ProjectType.Django };

    public override ServiceCommand? BuildServiceCommand(ToolExecutionContext context)
        => new(Id, DisplayName, context.ProjectPath, "python", new[] { "manage.py", "runserver" }, "python", AnnouncesApplicationUrl: true,
            EnvironmentVariables: new Dictionary<string, string> { ["PYTHONUNBUFFERED"] = "1" });
}

/// <summary>.NET application run with hot reload (dotnet watch run).</summary>
public sealed class DotNetWatchTool : ServiceTool
{
    public override string Id => ToolIds.DotNetWatch;
    public override string DisplayName => "dotnet watch";
    public override string Icon => "🟪";
    public override ToolCategory Category => ToolCategories.ProjectServices;
    public override IReadOnlyCollection<ProjectType>? SupportedProjectTypes => new[] { ProjectType.DotNet };

    public override ServiceCommand? BuildServiceCommand(ToolExecutionContext context)
    {
        var dotNetProjectPath = ProjectScanner.FindDotNetProject(context.ProjectPath);
        if (dotNetProjectPath is null)
        {
            context.Log.Error("❌ Aucun fichier .csproj trouvé — dotnet watch ne sera pas lancé.");
            return null;
        }
        // DevLauncher opens the browser itself once the URL is announced.
        return new ServiceCommand(Id, DisplayName, Path.GetDirectoryName(dotNetProjectPath) ?? context.ProjectPath, "dotnet", new[] { "watch", "run" }, "dotnet",
            AnnouncesApplicationUrl: true,
            EnvironmentVariables: new Dictionary<string, string> { ["DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER"] = "1" });
    }
}
