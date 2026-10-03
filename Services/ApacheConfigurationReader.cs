using System.IO;
using System.Text.RegularExpressions;

namespace DevLauncher.Services;

/// <summary>Reads the ports Apache listens on from the « Listen » directives of its configuration (httpd.conf and its SSL file).</summary>
public static partial class ApacheConfigurationReader
{
    /// <summary>Returns the listened ports of the XAMPP Apache, or the configured web port when the configuration cannot be read.</summary>
    public static IReadOnlyList<int> ReadListenPorts(string xamppDirectory, int fallbackPort)
    {
        var configurationDirectory = Path.Combine(xamppDirectory, "apache", "conf");
        var mainConfigurationPath = Path.Combine(configurationDirectory, "httpd.conf");
        try
        {
            if (!File.Exists(mainConfigurationPath)) return new[] { fallbackPort };
            var mainConfigurationLines = File.ReadAllLines(mainConfigurationPath);
            var configurationLines = mainConfigurationLines.AsEnumerable();
            var sslConfigurationPath = Path.Combine(configurationDirectory, "extra", "httpd-ssl.conf");
            if (IncludesSslConfiguration(mainConfigurationLines) && File.Exists(sslConfigurationPath))
                configurationLines = configurationLines.Concat(File.ReadAllLines(sslConfigurationPath));
            var listenPorts = ParseListenPorts(configurationLines);
            return listenPorts.Count > 0 ? listenPorts : new[] { fallbackPort };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new[] { fallbackPort };
        }
    }

    /// <summary>Reads the ports of the active « Listen [address:]port » directives, without duplicates.</summary>
    public static IReadOnlyList<int> ParseListenPorts(IEnumerable<string> configurationLines)
        => configurationLines
            .Select(configurationLine => ListenDirectiveRegex().Match(configurationLine))
            .Where(listenMatch => listenMatch.Success)
            .Select(listenMatch => int.Parse(listenMatch.Groups["port"].Value))
            .Where(SettingsService.IsValidPort)
            .Distinct()
            .ToList();

    private static bool IncludesSslConfiguration(IEnumerable<string> mainConfigurationLines)
        => mainConfigurationLines.Any(configurationLine => SslIncludeRegex().IsMatch(configurationLine));

    [GeneratedRegex(@"^\s*Listen\s+(?:\S*:)?(?<port>\d{1,5})\b", RegexOptions.IgnoreCase)]
    private static partial Regex ListenDirectiveRegex();

    [GeneratedRegex(@"^\s*Include\s+""?conf/extra/httpd-ssl\.conf", RegexOptions.IgnoreCase)]
    private static partial Regex SslIncludeRegex();
}
