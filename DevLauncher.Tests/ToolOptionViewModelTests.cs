using DevLauncher.Services.Tools;
using DevLauncher.ViewModels;
using Xunit;

namespace DevLauncher.Tests;

public class ToolOptionViewModelTests
{
    [Fact]
    public void ChoicesDependOnTheProjectAndKeepTheSelection()
    {
        var optionDefinition = new ToolOptionDefinition("session", "Session :", ToolOptionKind.SingleChoice,
            optionContext => optionContext.ProjectPath is null
                ? new[] { new ToolOptionChoice("new", "Nouvelle") }
                : new[] { new ToolOptionChoice("new", "Nouvelle"), new ToolOptionChoice("abc", "Session abc") },
            _ => new[] { "new" });
        var optionViewModel = new ToolOptionViewModel(optionDefinition);
        Assert.Single(optionViewModel.Choices);
        optionViewModel.ReloadChoices(new ToolOptionContext(@"C:\projet"));
        Assert.Equal(2, optionViewModel.Choices.Count);
        optionViewModel.ApplyValues(new[] { "abc" });
        Assert.Equal("abc", optionViewModel.SelectedChoice?.Value);
        optionViewModel.ReloadChoices(new ToolOptionContext(@"C:\projet"));
        Assert.Equal(new[] { "abc" }, optionViewModel.CaptureValues());
        optionViewModel.ApplyValues(new[] { "removed" });
        Assert.Equal("new", optionViewModel.SelectedChoice?.Value);
    }

    [Fact]
    public void MultipleChoiceFallsBackToDefaults()
    {
        var optionDefinition = new ToolOptionDefinition("targets", "Navigateurs :", ToolOptionKind.MultipleChoice,
            _ => new[] { new ToolOptionChoice("default", "Défaut"), new ToolOptionChoice("chrome", "Chrome") },
            _ => new[] { "default" });
        var optionViewModel = new ToolOptionViewModel(optionDefinition);
        optionViewModel.ApplyValues(Array.Empty<string>());
        Assert.Equal(new[] { "default" }, optionViewModel.CaptureValues());
        optionViewModel.ApplyValues(new[] { "chrome", "default" });
        Assert.Equal(new[] { "default", "chrome" }, optionViewModel.CaptureValues());
    }
}
