namespace DevLauncher.Models;

/// <summary>Kind of project, deciding which tools are available and how the project URL is found.</summary>
public enum ProjectType
{
    /// <summary>PHP or static site served by Apache.</summary>
    Other,
    Symfony,
    Laravel,
    WordPress,
    Node,
    Django,
    DotNet,
}

/// <summary>Display names of the project types.</summary>
public static class ProjectTypeLabels
{
    public static IReadOnlyList<(ProjectType ProjectType, string Label)> All { get; } = new[]
    {
        (ProjectType.Symfony, "⚡ Symfony"),
        (ProjectType.Laravel, "🔺 Laravel"),
        (ProjectType.WordPress, "📰 WordPress"),
        (ProjectType.Node, "🟩 Node.js"),
        (ProjectType.Django, "🐍 Django"),
        (ProjectType.DotNet, "🟪 .NET"),
        (ProjectType.Other, "📦 Autre (PHP / HTML…)"),
    };

    public static string GetName(ProjectType projectType) => projectType switch
    {
        ProjectType.Symfony => "Symfony",
        ProjectType.Laravel => "Laravel",
        ProjectType.WordPress => "WordPress",
        ProjectType.Node => "Node.js",
        ProjectType.Django => "Django",
        ProjectType.DotNet => ".NET",
        _ => "PHP / HTML",
    };
}
