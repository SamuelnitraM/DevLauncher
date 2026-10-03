using System.IO;
using System.Text.RegularExpressions;
using DevLauncher.Models;
using DevLauncher.Services.Stacks;

namespace DevLauncher.Services.Tools;

/// <summary>Containers of the compose file of the project : « docker compose up -d » at launch, « docker compose down » at stop.</summary>
public sealed class DockerComposeTool : LaunchTool
{
    private static readonly TimeSpan ComposeTimeout = TimeSpan.FromMinutes(10);

    public override string Id => ToolIds.DockerCompose;
    public override string DisplayName => "Docker Compose";
    public override string Icon => "🐳";
    public override ToolCategory Category => ToolCategories.ProjectServices;
    public override LaunchStage Stage => LaunchStage.Infrastructure;

    /// <summary>Offered only when Docker is installed.</summary>
    public override bool IsEnabledInSettings => ExecutableLocator.FindInPath("docker") is not null;

    public override async Task<ToolStartResult> StartAsync(ToolExecutionContext context)
    {
        var composeFilePath = ProjectScanner.FindComposeFile(context.ProjectPath);
        if (composeFilePath is null)
        {
            context.Log.Error("❌ Aucun fichier compose.yaml ou docker-compose.yml à la racine du projet");
            return ToolStartResult.Failed;
        }
        context.Log.Info($"🐳 docker compose up -d ({Path.GetFileName(composeFilePath)})…");
        var exitCode = await RunComposeAsync(context, "up -d");
        if (exitCode != 0)
        {
            context.Log.Error("❌ docker compose up a échoué : Docker Desktop est-il démarré ?");
            return ToolStartResult.Failed;
        }
        context.Log.Info("🐳 État des conteneurs :");
        await RunComposeAsync(context, "ps --format \"table {{.Name}}\\t{{.State}}\\t{{.Ports}}\"");
        return ToolStartResult.Started;
    }

    public override async Task StopAsync(ToolExecutionContext context)
    {
        if (ProjectScanner.FindComposeFile(context.ProjectPath) is null) return;
        context.Log.Info($"⏹ docker compose down ({context.ProjectName})…");
        await RunComposeAsync(context, "down");
    }

    private static Task<int?> RunComposeAsync(ToolExecutionContext context, string composeArguments)
        => context.ProcessLauncher.RunCommandLineAsync($"docker compose {composeArguments}", context.ProjectPath, ComposeTimeout,
            (outputLine, isErrorStream) => PreLaunchCommandsTool.LogOutputLine(context.Log, outputLine, isErrorStream));
}

/// <summary>
/// MySQL database of the project, read from its .env : created when missing, filled from a dump at its creation,
/// and opened in phpMyAdmin or HeidiSQL. Runs once MySQL answers, before the commands preparing the project.
/// </summary>
public sealed partial class DatabaseTool : LaunchTool
{
    private const string CreateAction = "create";
    private const string ImportAction = "import";
    private const string NoDump = "";
    private const string NoClient = "";
    private const string PhpMyAdminClient = "phpmyadmin";
    private const string HeidiSqlClient = "heidisql";
    private static readonly TimeSpan DatabaseCommandTimeout = TimeSpan.FromMinutes(10);
    private static readonly string[] _dumpFolderNames = { ".", "database", "db", "dump", "dumps", "sql", "data", "var" };

    public override string Id => ToolIds.Database;
    public override string DisplayName => "Base de données du projet";
    public override string Icon => "🗃️";
    public override ToolCategory Category => ToolCategories.Utilities;
    public override LaunchStage Stage => LaunchStage.Preparation;

    public override IReadOnlyList<ToolOptionDefinition> Options { get; } = new[]
    {
        new ToolOptionDefinition(ToolIds.DatabaseActionsOption, "Actions :", ToolOptionKind.MultipleChoice,
            _ => new[]
            {
                new ToolOptionChoice(CreateAction, "Créer la base si elle n'existe pas"),
                new ToolOptionChoice(ImportAction, "Importer le dump choisi quand la base est créée"),
            },
            _ => new[] { CreateAction }),
        new ToolOptionDefinition(ToolIds.DatabaseDumpOption, "Dump (.sql) :", ToolOptionKind.SingleChoice,
            optionContext => new[] { new ToolOptionChoice(NoDump, "Aucun") }
                .Concat(optionContext.ProjectPath is null ? Array.Empty<ToolOptionChoice>() : FindDumpFiles(optionContext.ProjectPath)
                    .Select(dumpRelativePath => new ToolOptionChoice(dumpRelativePath, dumpRelativePath)))
                .ToList(),
            _ => new[] { NoDump }),
        new ToolOptionDefinition(ToolIds.DatabaseClientOption, "Ouvrir avec :", ToolOptionKind.SingleChoice,
            _ => GetClientChoices(), _ => new[] { NoClient }),
    };

    public override async Task<ToolStartResult> StartAsync(ToolExecutionContext context)
    {
        var databaseConnection = DatabaseConnectionReader.ReadFromProject(context.ProjectPath);
        if (databaseConnection is null)
        {
            context.Log.Error("❌ Aucune base configurée dans le .env du projet (DATABASE_URL ou DB_DATABASE)");
            return ToolStartResult.Failed;
        }
        if (!databaseConnection.IsMySql)
        {
            context.Log.Info($"ℹ️ Base « {databaseConnection.DatabaseName} » en {databaseConnection.Engine} : seules MySQL et MariaDB sont gérées");
            return ToolStartResult.Started;
        }
        if (!IsSafeDatabaseName(databaseConnection.DatabaseName))
        {
            context.Log.Error($"❌ Nom de base « {databaseConnection.DatabaseName} » non pris en charge (lettres, chiffres, _ , - et $ uniquement)");
            return ToolStartResult.Failed;
        }
        await MySqlReadiness.WaitForMySqlAsync(context);
        var selectedActions = context.GetOptionValues(ToolIds.DatabaseActionsOption);
        var isStarted = true;
        if (selectedActions.Contains(CreateAction))
        {
            var databaseExists = await DatabaseExistsAsync(context, databaseConnection);
            if (databaseExists is null) return ToolStartResult.Failed;
            if (databaseExists == true)
            {
                context.Log.Info($"🗃️ Base « {databaseConnection.DatabaseName} » déjà présente");
            }
            else
            {
                isStarted = await CreateDatabaseAsync(context, databaseConnection);
                var dumpRelativePath = context.GetOptionValues(ToolIds.DatabaseDumpOption).FirstOrDefault();
                if (isStarted && selectedActions.Contains(ImportAction) && !string.IsNullOrEmpty(dumpRelativePath))
                    isStarted = await ImportDumpAsync(context, databaseConnection, dumpRelativePath);
            }
        }
        OpenClient(context, databaseConnection, context.GetOptionValues(ToolIds.DatabaseClientOption).FirstOrDefault() ?? NoClient);
        return isStarted ? ToolStartResult.Started : ToolStartResult.Failed;
    }

    /// <summary>Returns whether the database exists, null when MySQL cannot be queried.</summary>
    private static async Task<bool?> DatabaseExistsAsync(ToolExecutionContext context, DatabaseConnection databaseConnection)
    {
        var outputLines = new List<string>();
        var exitCode = await RunMySqlClientAsync(context, databaseConnection, $"-N -e \"SHOW DATABASES LIKE '{databaseConnection.DatabaseName}'\"",
            (outputLine, isErrorStream) =>
            {
                if (isErrorStream) context.Log.Error($"   │ {outputLine}");
                else outputLines.Add(outputLine.Trim());
            });
        if (exitCode == 0) return outputLines.Contains(databaseConnection.DatabaseName, StringComparer.OrdinalIgnoreCase);
        context.Log.Error($"❌ MySQL n'a pas répondu ({databaseConnection.User}@{databaseConnection.Host}:{databaseConnection.Port}) : base non vérifiée");
        return null;
    }

    private static async Task<bool> CreateDatabaseAsync(ToolExecutionContext context, DatabaseConnection databaseConnection)
    {
        context.Log.Info($"🗃️ Création de la base « {databaseConnection.DatabaseName} »…");
        var exitCode = await RunMySqlClientAsync(context, databaseConnection,
            $"-e \"CREATE DATABASE IF NOT EXISTS `{databaseConnection.DatabaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci\"",
            (outputLine, isErrorStream) => PreLaunchCommandsTool.LogOutputLine(context.Log, outputLine, isErrorStream));
        if (exitCode == 0) context.Log.Info("   ✅ Base créée");
        else context.Log.Error("   ❌ Création impossible");
        return exitCode == 0;
    }

    private static async Task<bool> ImportDumpAsync(ToolExecutionContext context, DatabaseConnection databaseConnection, string dumpRelativePath)
    {
        var dumpFilePath = Path.GetFullPath(Path.Combine(context.ProjectPath, dumpRelativePath));
        if (!File.Exists(dumpFilePath))
        {
            context.Log.Error($"   ❌ Dump introuvable : {dumpRelativePath}");
            return false;
        }
        context.Log.Info($"🗃️ Import de {dumpRelativePath}…");
        var exitCode = await RunMySqlClientAsync(context, databaseConnection, $"{databaseConnection.DatabaseName} < \"{dumpFilePath}\"",
            (outputLine, isErrorStream) => PreLaunchCommandsTool.LogOutputLine(context.Log, outputLine, isErrorStream));
        if (exitCode == 0) context.Log.Info("   ✅ Dump importé");
        else context.Log.Error("   ❌ Import en échec");
        return exitCode == 0;
    }

    /// <summary>Runs the MySQL client of XAMPP (or the one of the PATH). The password goes through MYSQL_PWD, never on the command line.</summary>
    private static Task<int?> RunMySqlClientAsync(ToolExecutionContext context, DatabaseConnection databaseConnection, string clientArguments, Action<string, bool> onOutputLine)
    {
        var commandLine = $"\"{FindMySqlClient()}\" --host={databaseConnection.Host} --port={databaseConnection.Port} --user={databaseConnection.User} {clientArguments}";
        return context.ProcessLauncher.RunCommandLineAsync(commandLine, context.ProjectPath, DatabaseCommandTimeout, onOutputLine,
            new Dictionary<string, string> { ["MYSQL_PWD"] = databaseConnection.Password });
    }

    private static void OpenClient(ToolExecutionContext context, DatabaseConnection databaseConnection, string client)
    {
        switch (client)
        {
            case PhpMyAdminClient:
                var phpMyAdminUrl = $"http://localhost{(AppSettings.LocalWebPort == 80 ? string.Empty : $":{AppSettings.LocalWebPort}")}/phpmyadmin/index.php?db={Uri.EscapeDataString(databaseConnection.DatabaseName)}";
                context.Log.Info($"🗃️ Ouverture de phpMyAdmin : {phpMyAdminUrl}");
                context.ProcessLauncher.StartShellProcess(phpMyAdminUrl);
                break;
            case HeidiSqlClient when FindHeidiSql() is { } heidiSqlPath:
                context.Log.Info("🗃️ Ouverture de HeidiSQL…");
                context.ProcessLauncher.StartShellProcess(heidiSqlPath,
                    $"--host={databaseConnection.Host} --port={databaseConnection.Port} --user={databaseConnection.User} --password=\"{databaseConnection.Password}\" --databases={databaseConnection.DatabaseName}");
                break;
        }
    }

    /// <summary>Lists the .sql files at the root of the project and in its usual dump folders, as paths relative to the project.</summary>
    public static IReadOnlyList<string> FindDumpFiles(string projectPath)
    {
        var dumpRelativePaths = new List<string>();
        foreach (var dumpFolderName in _dumpFolderNames)
        {
            var dumpFolderPath = Path.Combine(projectPath, dumpFolderName);
            try
            {
                if (!Directory.Exists(dumpFolderPath)) continue;
                dumpRelativePaths.AddRange(Directory.EnumerateFiles(dumpFolderPath, "*.sql")
                    .Select(dumpFilePath => Path.GetRelativePath(projectPath, dumpFilePath))
                    .OrderBy(dumpRelativePath => dumpRelativePath, StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // An unreadable folder is skipped.
            }
        }
        return dumpRelativePaths;
    }

    private static IReadOnlyList<ToolOptionChoice> GetClientChoices()
    {
        var clientChoices = new List<ToolOptionChoice> { new(NoClient, "Aucun"), new(PhpMyAdminClient, "phpMyAdmin (XAMPP)") };
        if (FindHeidiSql() is not null) clientChoices.Add(new ToolOptionChoice(HeidiSqlClient, "HeidiSQL"));
        return clientChoices;
    }

    private static string FindMySqlClient()
    {
        var xamppClientPath = Path.Combine(Path.GetDirectoryName(AppSettings.MySQLExe) ?? string.Empty, "mysql.exe");
        return File.Exists(xamppClientPath) ? xamppClientPath : "mysql";
    }

    private static string? FindHeidiSql()
        => new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(Environment.GetFolderPath)
            .Where(programFilesDirectory => !string.IsNullOrEmpty(programFilesDirectory))
            .Select(programFilesDirectory => Path.Combine(programFilesDirectory, "HeidiSQL", "heidisql.exe"))
            .FirstOrDefault(File.Exists);

    /// <summary>The name is written inside SQL and command lines : only plain identifiers are accepted.</summary>
    public static bool IsSafeDatabaseName(string databaseName) => SafeDatabaseNameRegex().IsMatch(databaseName);

    [GeneratedRegex(@"^[A-Za-z0-9_$\-]{1,64}$")]
    private static partial Regex SafeDatabaseNameRegex();
}
