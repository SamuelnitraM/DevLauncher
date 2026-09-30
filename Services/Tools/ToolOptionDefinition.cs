namespace DevLauncher.Services.Tools;

public enum ToolOptionKind
{
    SingleChoice,
    MultipleChoice,
}

public sealed record ToolOptionChoice(string Value, string Label);

/// <summary>What the choices of an option can depend on : the selected project, when there is one.</summary>
public sealed record ToolOptionContext(string? ProjectPath);

/// <summary>
/// Option of a tool. Choices and default values are read each time the options panel is refreshed
/// (project selected, settings saved), so they can depend on the project and on the settings.
/// </summary>
/// <param name="GetDefaultValues">Values used when the profile holds none. A single-choice option falls back to its first choice.</param>
public sealed record ToolOptionDefinition(
    string Key,
    string Label,
    ToolOptionKind Kind,
    Func<ToolOptionContext, IReadOnlyList<ToolOptionChoice>> GetChoices,
    Func<ToolOptionContext, IReadOnlyList<string>> GetDefaultValues);
