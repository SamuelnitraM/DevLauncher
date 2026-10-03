using System.Text;
using System.Text.RegularExpressions;

namespace DevLauncher.Services.Hosting;

/// <summary>Piece of an output line sharing the same style. A null color keeps the default color of the log.</summary>
public sealed record AnsiSegment(string Text, string? ForegroundColor, bool IsBold);

/// <summary>Output line of a service : its text without escape sequences, its error flag and its styled segments.</summary>
public sealed record ServiceOutputLine(string Text, bool IsError, IReadOnlyList<AnsiSegment> Segments)
{
    /// <summary>Line written by DevLauncher itself, without style.</summary>
    public static ServiceOutputLine FromMessage(string message, bool isError) => new(message, isError, new[] { new AnsiSegment(message, null, false) });

    /// <summary>Line printed by a service, classified as an error when it mentions an error.</summary>
    public static ServiceOutputLine FromRawOutput(string rawLine)
    {
        var segments = AnsiTextParser.Parse(rawLine);
        var text = string.Concat(segments.Select(segment => segment.Text));
        return new ServiceOutputLine(text, OutputLineClassifier.LooksLikeError(text), segments);
    }
}

/// <summary>
/// Splits a line holding ANSI escape sequences into styled segments. Colors (16 colors, 256 colors and true colors)
/// and bold are kept ; the other sequences (cursor moves, screen clearing) are dropped.
/// </summary>
public static partial class AnsiTextParser
{
    /// <summary>Standard and bright colors, lightened to stay readable on the dark log background.</summary>
    private static readonly string[] _basicColors =
    {
        "#6E6E8A", "#FF6B6B", "#3DDC84", "#F5C542", "#4F9EFF", "#D27CFF", "#3ED6D6", "#D8D8E4",
        "#8888AA", "#FF8E8E", "#6BF0A5", "#FFE07A", "#7DB8FF", "#E3A3FF", "#7CF0F0", "#FFFFFF",
    };

    public static IReadOnlyList<AnsiSegment> Parse(string rawLine)
    {
        var segments = new List<AnsiSegment>();
        string? currentColor = null;
        var isBold = false;
        var segmentText = new StringBuilder();
        var textStart = 0;
        void FlushSegment()
        {
            if (segmentText.Length == 0) return;
            segments.Add(new AnsiSegment(segmentText.ToString(), currentColor, isBold));
            segmentText.Clear();
        }
        foreach (Match escapeMatch in EscapeSequenceRegex().Matches(rawLine))
        {
            segmentText.Append(rawLine, textStart, escapeMatch.Index - textStart);
            textStart = escapeMatch.Index + escapeMatch.Length;
            if (escapeMatch.Groups["final"].Value != "m") continue;
            FlushSegment();
            ApplyGraphicParameters(escapeMatch.Groups["parameters"].Value, ref currentColor, ref isBold);
        }
        segmentText.Append(rawLine, textStart, rawLine.Length - textStart);
        FlushSegment();
        return segments;
    }

    /// <summary>Applies the parameters of a « select graphic rendition » sequence to the current style.</summary>
    private static void ApplyGraphicParameters(string parameterText, ref string? currentColor, ref bool isBold)
    {
        var parameters = parameterText.Length == 0
            ? new[] { 0 }
            : parameterText.Split(';').Select(parameter => int.TryParse(parameter, out var value) ? value : 0).ToArray();
        for (var parameterIndex = 0; parameterIndex < parameters.Length; parameterIndex++)
        {
            var parameter = parameters[parameterIndex];
            switch (parameter)
            {
                case 0:
                    currentColor = null;
                    isBold = false;
                    break;
                case 1:
                    isBold = true;
                    break;
                case 22:
                    isBold = false;
                    break;
                case >= 30 and <= 37:
                    currentColor = _basicColors[parameter - 30];
                    break;
                case >= 90 and <= 97:
                    currentColor = _basicColors[parameter - 90 + 8];
                    break;
                case 39:
                    currentColor = null;
                    break;
                case 38 when parameterIndex + 2 < parameters.Length && parameters[parameterIndex + 1] == 5:
                    currentColor = ConvertPaletteColor(parameters[parameterIndex + 2]);
                    parameterIndex += 2;
                    break;
                case 38 when parameterIndex + 4 < parameters.Length && parameters[parameterIndex + 1] == 2:
                    currentColor = $"#{Math.Clamp(parameters[parameterIndex + 2], 0, 255):X2}{Math.Clamp(parameters[parameterIndex + 3], 0, 255):X2}{Math.Clamp(parameters[parameterIndex + 4], 0, 255):X2}";
                    parameterIndex += 4;
                    break;
                case 48 when parameterIndex + 2 < parameters.Length && parameters[parameterIndex + 1] == 5:
                    parameterIndex += 2;
                    break;
                case 48 when parameterIndex + 4 < parameters.Length && parameters[parameterIndex + 1] == 2:
                    parameterIndex += 4;
                    break;
            }
        }
    }

    /// <summary>Color of the 256-color palette : the 16 basic colors, the 6×6×6 cube, then 24 grays.</summary>
    public static string ConvertPaletteColor(int paletteIndex)
    {
        if (paletteIndex is >= 0 and < 16) return _basicColors[paletteIndex];
        if (paletteIndex is >= 16 and < 232)
        {
            var cubeIndex = paletteIndex - 16;
            static int ToChannel(int level) => level == 0 ? 0 : 55 + level * 40;
            return $"#{ToChannel(cubeIndex / 36):X2}{ToChannel(cubeIndex / 6 % 6):X2}{ToChannel(cubeIndex % 6):X2}";
        }
        var grayLevel = 8 + (Math.Clamp(paletteIndex, 232, 255) - 232) * 10;
        return $"#{grayLevel:X2}{grayLevel:X2}{grayLevel:X2}";
    }

    [GeneratedRegex(@"\x1B\[(?<parameters>[0-9;?]*)[ -/]*(?<final>[@-~])")]
    private static partial Regex EscapeSequenceRegex();
}
