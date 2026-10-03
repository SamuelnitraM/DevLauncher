using System.IO;

namespace DevLauncher.Services.Stacks;

/// <summary>Database of a project, as configured in its environment files.</summary>
public sealed record DatabaseConnection(string Engine, string Host, int Port, string User, string Password, string DatabaseName)
{
    public bool IsMySql => Engine is "mysql" or "mariadb";
}

/// <summary>
/// Reads the database of a project from its .env files : DATABASE_URL (Symfony, Doctrine) or the DB_* variables (Laravel).
/// The local files (.env.local, .env.dev.local) override .env, as in Symfony.
/// </summary>
public static class DatabaseConnectionReader
{
    private static readonly string[] _environmentFileNames = { ".env", ".env.local", ".env.dev", ".env.dev.local" };

    public static DatabaseConnection? ReadFromProject(string projectPath)
    {
        var environmentVariables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var environmentFileName in _environmentFileNames)
        {
            var environmentFilePath = Path.Combine(projectPath, environmentFileName);
            try
            {
                if (!File.Exists(environmentFilePath)) continue;
                foreach (var (variableName, variableValue) in ParseEnvironmentFile(File.ReadAllLines(environmentFilePath)))
                    environmentVariables[variableName] = variableValue;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // An unreadable environment file is skipped.
            }
        }
        return FromEnvironment(environmentVariables);
    }

    /// <summary>DATABASE_URL first, then the Laravel variables.</summary>
    public static DatabaseConnection? FromEnvironment(IReadOnlyDictionary<string, string> environmentVariables)
    {
        if (environmentVariables.TryGetValue("DATABASE_URL", out var databaseUrl) && ParseDatabaseUrl(databaseUrl) is { } urlConnection) return urlConnection;
        if (!environmentVariables.TryGetValue("DB_DATABASE", out var databaseName) || string.IsNullOrWhiteSpace(databaseName)) return null;
        var engine = environmentVariables.GetValueOrDefault("DB_CONNECTION", "mysql").ToLowerInvariant();
        var port = int.TryParse(environmentVariables.GetValueOrDefault("DB_PORT"), out var configuredPort) ? configuredPort : MySqlConfigurationReader.DefaultPort;
        return new DatabaseConnection(engine, environmentVariables.GetValueOrDefault("DB_HOST", "127.0.0.1"), port,
            environmentVariables.GetValueOrDefault("DB_USERNAME", "root"), environmentVariables.GetValueOrDefault("DB_PASSWORD", string.Empty), databaseName);
    }

    /// <summary>Reads « mysql://user:password@host:port/database?serverVersion=… ».</summary>
    public static DatabaseConnection? ParseDatabaseUrl(string databaseUrl)
    {
        if (!Uri.TryCreate(databaseUrl.Trim(), UriKind.Absolute, out var databaseUri)) return null;
        var databaseName = Uri.UnescapeDataString(databaseUri.AbsolutePath.Trim('/'));
        if (databaseName.Length == 0) return null;
        var userInformationParts = databaseUri.UserInfo.Split(':', 2);
        var user = Uri.UnescapeDataString(userInformationParts[0]);
        var password = userInformationParts.Length == 2 ? Uri.UnescapeDataString(userInformationParts[1]) : string.Empty;
        var port = databaseUri.IsDefaultPort || databaseUri.Port <= 0 ? MySqlConfigurationReader.DefaultPort : databaseUri.Port;
        return new DatabaseConnection(databaseUri.Scheme.ToLowerInvariant(), databaseUri.Host, port, user.Length == 0 ? "root" : user, password, databaseName);
    }

    /// <summary>Reads « NAME=value » lines : comments, « export » prefixes and quotes are handled, variable expansions are kept as written.</summary>
    public static IEnumerable<(string Name, string Value)> ParseEnvironmentFile(IEnumerable<string> environmentLines)
    {
        foreach (var rawLine in environmentLines)
        {
            var environmentLine = rawLine.Trim();
            if (environmentLine.Length == 0 || environmentLine.StartsWith('#')) continue;
            if (environmentLine.StartsWith("export ", StringComparison.Ordinal)) environmentLine = environmentLine["export ".Length..].TrimStart();
            var separatorIndex = environmentLine.IndexOf('=');
            if (separatorIndex <= 0) continue;
            var variableName = environmentLine[..separatorIndex].Trim();
            var variableValue = environmentLine[(separatorIndex + 1)..].Trim();
            var closingQuoteIndex = variableValue.Length >= 2 && variableValue[0] is '"' or '\'' ? variableValue.IndexOf(variableValue[0], 1) : -1;
            if (closingQuoteIndex > 0)
            {
                // A quoted value ends at its closing quote : what follows is a comment.
                variableValue = variableValue[1..closingQuoteIndex];
            }
            else
            {
                var commentIndex = variableValue.IndexOf(" #", StringComparison.Ordinal);
                if (commentIndex >= 0) variableValue = variableValue[..commentIndex].TrimEnd();
            }
            yield return (variableName, variableValue);
        }
    }
}
