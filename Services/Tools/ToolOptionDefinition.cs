namespace DevLauncher.Services.Tools;

public enum ToolOptionKind
{
    SingleChoice,
    MultipleChoice,
}

public sealed record ToolOptionChoice(string Value, string Label);

/// <summary>
/// Option of a tool. Choices are read when the options panel is built, so they can depend on the settings.
/// </summary>
/// <param name="DefaultValues">Values used when the profile holds none. A single-choice option falls back to its first choice.</param>
public sealed record ToolOptionDefinition(
    string Key,
    string Label,
    ToolOptionKind Kind,
    Func<IReadOnlyList<ToolOptionChoice>> GetChoices,
    IReadOnlyList<string> DefaultValues);
