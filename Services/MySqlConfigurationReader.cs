using System.IO;

namespace DevLauncher.Services;

/// <summary>Reads the port of the MySQL server from its configuration file (my.ini).</summary>
public static class MySqlConfigurationReader
{
    public const int DefaultPort = 3306;

    /// <summary>Returns the port of the [mysqld] section, or the default port when the file or the setting is missing.</summary>
    public static int ReadServerPort(string configurationFilePath)
    {
        try
        {
            return File.Exists(configurationFilePath) ? ParseServerPort(File.ReadAllLines(configurationFilePath)) : DefaultPort;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return DefaultPort;
        }
    }

    public static int ParseServerPort(IEnumerable<string> configurationLines)
    {
        var isInServerSection = false;
        foreach (var rawLine in configurationLines)
        {
            var configurationLine = rawLine.Trim();
            if (configurationLine.StartsWith('['))
            {
                isInServerSection = configurationLine.Equals("[mysqld]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!isInServerSection || configurationLine.StartsWith('#') || configurationLine.StartsWith(';')) continue;
            var settingParts = configurationLine.Split('=', 2, StringSplitOptions.TrimEntries);
            if (settingParts.Length == 2 && settingParts[0].Equals("port", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(settingParts[1], out var port) && SettingsService.IsValidPort(port))
                return port;
        }
        return DefaultPort;
    }
}
