using DevLauncher.Services.Hosting;
using Xunit;

namespace DevLauncher.Tests;

public class AnsiTextParserTests
{
    [Fact]
    public void ColorsAndBoldAreSplitIntoSegments()
    {
        var segments = AnsiTextParser.Parse("\u001b[32m  ➜ \u001b[39m \u001b[1mLocal\u001b[22m:   \u001b[36mhttp://localhost:\u001b[1m5173\u001b[22m/\u001b[39m");
        Assert.Equal("  ➜  Local:   http://localhost:5173/", string.Concat(segments.Select(segment => segment.Text)));
        Assert.Equal(new AnsiSegment("  ➜ ", "#3DDC84", false), segments[0]);
        Assert.Equal(new AnsiSegment(" ", null, false), segments[1]);
        Assert.Equal(new AnsiSegment("Local", null, true), segments[2]);
        Assert.Equal(new AnsiSegment("5173", "#3ED6D6", true), segments[5]);
    }

    [Fact]
    public void ExtendedColorsAndResetAreApplied()
    {
        var segments = AnsiTextParser.Parse("\u001b[38;5;196mrouge\u001b[0m normal \u001b[38;2;10;20;300mvrai\u001b[m");
        Assert.Equal(new[] { "#FF0000", null, "#0A14FF" }, segments.Select(segment => segment.ForegroundColor));
        Assert.Equal("#080808", AnsiTextParser.ConvertPaletteColor(232));
        Assert.Equal("#FF6B6B", AnsiTextParser.ConvertPaletteColor(1));
    }

    [Fact]
    public void CursorAndScreenSequencesAreDropped()
    {
        var segments = AnsiTextParser.Parse("\u001b[2J\u001b[3J\u001b[HVITE ready in 312 ms\u001b[?25l");
        Assert.Equal(new AnsiSegment("VITE ready in 312 ms", null, false), Assert.Single(segments));
        Assert.Empty(AnsiTextParser.Parse(string.Empty));
    }

    [Fact]
    public void OutputLineKeepsPlainTextForSearchAndErrors()
    {
        var outputLine = ServiceOutputLine.FromRawOutput("\u001b[31m[ERROR]\u001b[0m Compilation failed");
        Assert.Equal("[ERROR] Compilation failed", outputLine.Text);
        Assert.True(outputLine.IsError);
        Assert.Equal(2, outputLine.Segments.Count);
    }
}
