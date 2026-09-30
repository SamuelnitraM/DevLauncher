namespace DevLauncher.Models;

/// <summary>State of a tool inside a profile : enabled or not, and the values of its options.</summary>
public class ToolSelection
{
    public bool IsEnabled { get; set; }

    /// <summary>Selected values by option key. A single-choice option holds at most one value.</summary>
    public Dictionary<string, List<string>> Options { get; set; } = new();

    public IReadOnlyList<string> GetOptionValues(string optionKey)
        => Options.TryGetValue(optionKey, out var optionValues) ? optionValues : Array.Empty<string>();

    public static ToolSelection Enabled(params (string OptionKey, string[] Values)[] optionValues) => new()
    {
        IsEnabled = true,
        Options = optionValues.ToDictionary(option => option.OptionKey, option => option.Values.ToList()),
    };
}
