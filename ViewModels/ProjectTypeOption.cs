using DevLauncher.Models;

namespace DevLauncher.ViewModels;

/// <summary>Project type offered in the project type dropdown.</summary>
public record ProjectTypeOption(ProjectType ProjectType, string Label)
{
    /// <summary>Name read by the accessibility tools and the UI automation.</summary>
    public override string ToString() => Label;
}
