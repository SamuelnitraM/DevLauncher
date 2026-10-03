using System.IO;
using DevLauncher.Models;

namespace DevLauncher.Services.Tools;

/// <summary>
/// Commands run before the services start : git pull, dependencies, migrations, cache, or a command typed by the user.
/// The commands offered depend on the files of the project. Migrations wait for MySQL when this launch starts it.
/// </summary>
public sealed class PreLaunchCommandsTool : LaunchTool
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Predefined command : stable identifier saved in the profiles, label, and condition on the project files.</summary>
    private sealed record PredefinedCommand(string Id, string Label, Func<string, bool> AppliesTo, Func<string> BuildCommandLine, bool NeedsDatabase = false);

    /// <summary>Predefined commands in their execution order : sources first, then dependencies, then database and cache.</summary>
    private static readonly PredefinedCommand[] _predefinedCommands =
    {
        new("git-pull", "git pull --ff-only", projectPath => GitStatusService.IsGitRepository(projectPath), () => "git pull --ff-only"),
        new("composer-install", "composer install", projectPath => HasFile(projectPath, "composer.json"), () => "composer install"),
        new("npm-install", "npm install", projectPath => HasFile(projectPath, "package.json"), () => "npm install"),
        new("pip-install", "pip install -r requirements.txt", projectPath => HasFile(projectPath, "requirements.txt"), () => "pip install -r requirements.txt"),
        new("dotnet-restore", "dotnet restore", projectPath => ProjectScanner.FindDotNetProject(projectPath) is not null, () => "dotnet restore"),
        new("doctrine-migrate", "doctrine:migrations:migrate", projectPath => HasFile(projectPath, "bin", "console"),
            () => $"{QuotedPhp()} bin/console doctrine:migrations:migrate --no-interaction --allow-no-migration", NeedsDatabase: true),
        new("artisan-migrate", "artisan migrate", projectPath => HasFile(projectPath, "artisan"), () => $"{QuotedPhp()} artisan migrate --force", NeedsDatabase: true),
        new("django-migrate", "manage.py migrate", projectPath => HasFile(projectPath, "manage.py"), () => "python manage.py migrate --noinput", NeedsDatabase: true),
        new("symfony-cache-clear", "cache:clear", projectPath => HasFile(projectPath, "bin", "console"), () => $"{QuotedPhp()} bin/console cache:clear"),
    };

    public override string Id => ToolIds.PreLaunchCommands;
    public override string DisplayName => "Commandes avant lancement";
    public override string Icon => "🧰";
    public override ToolCategory Category => ToolCategories.Utilities;
    public override LaunchStage Stage => LaunchStage.Preparation;

    public override IReadOnlyList<ToolOptionDefinition> Options { get; } = new[]
    {
        new ToolOptionDefinition(ToolIds.PreLaunchCommandsOption, "Commandes :", ToolOptionKind.MultipleChoice,
            optionContext => optionContext.ProjectPath is null
                ? Array.Empty<ToolOptionChoice>()
                : _predefinedCommands.Where(command => command.AppliesTo(optionContext.ProjectPath)).Select(command => new ToolOptionChoice(command.Id, command.Label)).ToList(),
            _ => Array.Empty<string>()),
        new ToolOptionDefinition(ToolIds.PreLaunchCustomCommandOption, "Commande personnalisée (exécutée en dernier) :", ToolOptionKind.Text,
            _ => Array.Empty<ToolOptionChoice>(), _ => Array.Empty<string>()),
    };

    public override async Task<ToolStartResult> StartAsync(ToolExecutionContext context)
    {
        var selectedCommandIds = context.GetOptionValues(ToolIds.PreLaunchCommandsOption).ToHashSet();
        var commandsToRun = _predefinedCommands
            .Where(command => selectedCommandIds.Contains(command.Id) && command.AppliesTo(context.ProjectPath))
            .Select(command => (CommandLine: command.BuildCommandLine(), command.NeedsDatabase))
            .ToList();
        var customCommandLine = context.GetOptionValues(ToolIds.PreLaunchCustomCommandOption).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(customCommandLine)) commandsToRun.Add((customCommandLine.Trim(), false));
        if (commandsToRun.Count == 0)
        {
            context.Log.Info("🧰 Aucune commande avant lancement cochée");
            return ToolStartResult.Started;
        }
        var failedCommandCount = 0;
        var isDatabaseChecked = false;
        foreach (var (commandLine, needsDatabase) in commandsToRun)
        {
            if (needsDatabase && !isDatabaseChecked)
            {
                await MySqlReadiness.WaitForMySqlAsync(context);
                isDatabaseChecked = true;
            }
            context.Log.Info($"🧰 {commandLine}");
            var exitCode = await context.ProcessLauncher.RunCommandLineAsync(commandLine, context.ProjectPath, CommandTimeout,
                (outputLine, isErrorStream) => LogOutputLine(context.Log, outputLine, isErrorStream));
            if (exitCode == 0)
            {
                context.Log.Info("   ✅ Terminé");
                continue;
            }
            failedCommandCount++;
            context.Log.Error(exitCode is null ? $"   ❌ « {commandLine} » n'a pas abouti" : $"   ❌ « {commandLine} » a échoué (code {exitCode}) — le lancement continue");
        }
        return failedCommandCount == 0 ? ToolStartResult.Started : ToolStartResult.Failed;
    }

    /// <summary>Output of the commands, indented under their command line. Error stream lines are shown as errors only when they look like errors : composer and npm write their progress there.</summary>
    public static void LogOutputLine(LaunchLog launchLog, string outputLine, bool isErrorStream)
    {
        if (string.IsNullOrWhiteSpace(outputLine)) return;
        var cleanLine = Hosting.OutputLineClassifier.RemoveAnsiSequences(outputLine);
        if (isErrorStream && Hosting.OutputLineClassifier.LooksLikeError(cleanLine)) launchLog.Error($"   │ {cleanLine}");
        else launchLog.Info($"   │ {cleanLine}");
    }

    private static bool HasFile(string projectPath, params string[] relativePathParts)
        => File.Exists(Path.Combine(new[] { projectPath }.Concat(relativePathParts).ToArray()));

    /// <summary>The PHP of XAMPP when present, quoted for cmd.exe.</summary>
    private static string QuotedPhp() => $"\"{AppSettings.PhpExecutable}\"";
}

/// <summary>Waits for the MySQL server of XAMPP before the steps that need the database.</summary>
public static class MySqlReadiness
{
    private static readonly TimeSpan DatabaseReadinessTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Waits for MySQL when this launch starts it, so that the database steps find it.</summary>
    public static async Task WaitForMySqlAsync(ToolExecutionContext context)
    {
        if (!context.Profile.IsToolEnabled(ToolIds.MySql)) return;
        var mySqlPort = MySqlConfigurationReader.ReadServerPort(AppSettings.MySQLConfig);
        if (await PortProbe.IsPortOpenAsync(mySqlPort)) return;
        context.Log.Info($"⏳ Attente de MySQL sur le port {mySqlPort}…");
        if (!await PortProbe.WaitForPortAsync(mySqlPort, DatabaseReadinessTimeout))
            context.Log.Error($"⚠️ MySQL ne répond pas après {DatabaseReadinessTimeout.TotalSeconds:0}s : les étapes de base de données risquent d'échouer");
    }
}
