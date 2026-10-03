using DevLauncher.Services;
using DevLauncher.ViewModels;
using Xunit;

namespace DevLauncher.Tests;

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Alt+D", HotkeyGesture.ControlModifier | HotkeyGesture.AltModifier, 0x44u)]
    [InlineData(" win + shift + 1 ", HotkeyGesture.WindowsModifier | HotkeyGesture.ShiftModifier, 0x31u)]
    [InlineData("Ctrl+F12", HotkeyGesture.ControlModifier, 0x7Bu)]
    [InlineData("Alt+Space", HotkeyGesture.AltModifier, 0x20u)]
    public void ValidGesturesAreParsed(string gestureText, uint expectedModifiers, uint expectedVirtualKey)
    {
        Assert.True(HotkeyGesture.TryParse(gestureText, out var hotkeyGesture));
        Assert.Equal(new HotkeyGesture(expectedModifiers, expectedVirtualKey), hotkeyGesture);
    }

    [Theory]
    [InlineData("")]
    [InlineData("D")]
    [InlineData("Ctrl+")]
    [InlineData("Hyper+D")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+Enter")]
    public void InvalidGesturesAreRejected(string gestureText) => Assert.False(HotkeyGesture.TryParse(gestureText, out _));
}

public class CommandPaletteViewModelTests
{
    private static PaletteCommand CreateCommand(string label) => new(label, "Test", () => Task.CompletedTask);

    [Fact]
    public void EveryTypedWordMustAppearIgnoringCaseAndAccents()
    {
        var paletteViewModel = new CommandPaletteViewModel(new[]
        {
            CreateCommand("🚀 Lancer templateSite"),
            CreateCommand("📁 Ouvrir templateSite"),
            CreateCommand("⚙️ Paramètres"),
            CreateCommand("🚀 Lancer boutique"),
        });
        Assert.Equal(4, paletteViewModel.FilteredCommands.Count);
        Assert.Equal("🚀 Lancer templateSite", paletteViewModel.SelectedCommand?.Label);
        paletteViewModel.SearchText = "temp LANC";
        Assert.Equal(new[] { "🚀 Lancer templateSite" }, paletteViewModel.FilteredCommands.Select(command => command.Label));
        paletteViewModel.SearchText = "parametres";
        Assert.Equal("⚙️ Paramètres", paletteViewModel.SelectedCommand?.Label);
        paletteViewModel.SearchText = "introuvable";
        Assert.Empty(paletteViewModel.FilteredCommands);
        Assert.Null(paletteViewModel.SelectedCommand);
    }

    [Fact]
    public void SelectionMovesAndWrapsAround()
    {
        var paletteViewModel = new CommandPaletteViewModel(new[] { CreateCommand("A"), CreateCommand("B"), CreateCommand("C") });
        paletteViewModel.MoveSelection(1);
        Assert.Equal("B", paletteViewModel.SelectedCommand?.Label);
        paletteViewModel.MoveSelection(2);
        Assert.Equal("A", paletteViewModel.SelectedCommand?.Label);
        paletteViewModel.MoveSelection(-1);
        Assert.Equal("C", paletteViewModel.SelectedCommand?.Label);
    }
}
