using DevLauncher.Services.Hosting;
using DevLauncher.ViewModels;
using Xunit;

namespace DevLauncher.Tests;

public class LogTabViewModelTests
{
    [Fact]
    public void SearchAndErrorFilterSelectTheVisibleLines()
    {
        var logTab = new LogTabViewModel("Journal");
        logTab.AppendLine("Server running on http://127.0.0.1:8000", false);
        logTab.AppendLine("GET /login 200", false);
        logTab.AppendLine("PHP Fatal error: Class not found", true, ServiceOutputLine.FromRawOutput("PHP Fatal error: Class not found").Segments);
        logTab.SearchText = "  get ";
        Assert.True(logTab.IsFiltered);
        Assert.Equal(new[] { "GET /login 200" }, logTab.VisibleLines.Cast<Models.LogEntry>().Select(logEntry => logEntry.Message));
        Assert.Equal("1 / 3 ligne(s)", logTab.FilterSummary);
        Assert.EndsWith("GET /login 200", logTab.GetText());
        logTab.ClearSearchCommand.Execute(null);
        logTab.ShowsErrorsOnly = true;
        Assert.Equal(new[] { true }, logTab.VisibleLines.Cast<Models.LogEntry>().Select(logEntry => logEntry.IsError));
        logTab.ShowsErrorsOnly = false;
        Assert.False(logTab.IsFiltered);
        Assert.Equal(3, logTab.VisibleLines.Cast<object>().Count());
    }
}
