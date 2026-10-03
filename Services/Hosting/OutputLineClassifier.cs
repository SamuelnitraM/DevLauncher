using System.Text.RegularExpressions;

namespace DevLauncher.Services.Hosting;

/// <summary>Cleans and classifies the lines printed by the commands and services.</summary>
public static partial class OutputLineClassifier
{
    public static string RemoveAnsiSequences(string outputLine) => AnsiEscapeSequenceRegex().Replace(outputLine, string.Empty);

    /// <summary>True when the line mentions an error, an exception or a failure.</summary>
    public static bool LooksLikeError(string outputLine) => ErrorMarkerRegex().IsMatch(outputLine);

    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex AnsiEscapeSequenceRegex();

    [GeneratedRegex(@"\b(error|erreur|exception|fatal|critical|failed)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorMarkerRegex();
}
