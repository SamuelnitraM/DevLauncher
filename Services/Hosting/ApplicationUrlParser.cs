using System.Text.RegularExpressions;

namespace DevLauncher.Services.Hosting;

/// <summary>
/// Finds the local URL announced by a development server in one of its output lines, for example
/// « Server running on [http://127.0.0.1:8000] », « ➜ Local: http://localhost:5173/ » or « Now listening on: http://localhost:5000 ».
/// </summary>
public static partial class ApplicationUrlParser
{
    /// <summary>Returns the local URL of the line, with a wildcard host replaced by localhost, or null.</summary>
    public static string? TryExtractLocalUrl(string outputLine)
    {
        var localUrlMatch = LocalUrlRegex().Match(outputLine);
        if (!localUrlMatch.Success) return null;
        var localUrl = localUrlMatch.Value.TrimEnd('.', ',', ';');
        return WildcardHostRegex().Replace(localUrl, "localhost");
    }

    [GeneratedRegex(@"https?://(?:localhost|127\.0\.0\.1|0\.0\.0\.0|\[::1?\])(?::\d+)?(?:/[^\s""'<>\]\)]*)?", RegexOptions.IgnoreCase)]
    private static partial Regex LocalUrlRegex();

    [GeneratedRegex(@"(?<=://)(?:0\.0\.0\.0|\[::\])")]
    private static partial Regex WildcardHostRegex();
}
