namespace DevLauncher.Services.Tools;

/// <summary>Group of tools shown as one card. In an exclusive category at most one tool is enabled.</summary>
/// <param name="NoneChoiceLabel">Label of the "no tool" choice of an exclusive category.</param>
public sealed record ToolCategory(string Id, string Title, bool IsExclusive, string? NoneChoiceLabel = null);

/// <summary>Categories in display order.</summary>
public static class ToolCategories
{
    public static readonly ToolCategory Editor = new("editor", "💻 Éditeur de code", true, "🚫 Aucun éditeur");
    public static readonly ToolCategory Symfony = new("symfony", "🔧 Services Symfony", false);
    public static readonly ToolCategory Xampp = new("xampp", "🗄️ Services XAMPP", false);
    public static readonly ToolCategory Utilities = new("utilities", "🛠️ Outils", false);

    public static IReadOnlyList<ToolCategory> All { get; } = new[] { Editor, Symfony, Xampp, Utilities };
}
