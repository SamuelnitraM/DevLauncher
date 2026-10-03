using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DevLauncher.ViewModels;

/// <summary>Action offered by the command palette.</summary>
public sealed record PaletteCommand(string Label, string Category, Func<Task> ExecuteAsync)
{
    /// <summary>Name read by the accessibility tools and the UI automation.</summary>
    public override string ToString() => Label;
}

/// <summary>
/// Command palette : every action of DevLauncher in a list filtered as the user types. All the typed words must appear
/// in the label, in any order, ignoring case and accents (« lanc temp » finds « Lancer templateSite »).
/// </summary>
public partial class CommandPaletteViewModel : ObservableObject
{
    private static readonly CompareInfo _comparer = CultureInfo.InvariantCulture.CompareInfo;
    private const CompareOptions SearchOptions = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    private readonly IReadOnlyList<PaletteCommand> _commands;

    public CommandPaletteViewModel(IReadOnlyList<PaletteCommand> commands)
    {
        _commands = commands;
        ApplyFilter();
    }

    public ObservableCollection<PaletteCommand> FilteredCommands { get; } = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private PaletteCommand? _selectedCommand;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    /// <summary>Moves the selection up or down the filtered list, wrapping around its ends.</summary>
    public void MoveSelection(int offset)
    {
        if (FilteredCommands.Count == 0) return;
        var currentIndex = SelectedCommand is null ? -1 : FilteredCommands.IndexOf(SelectedCommand);
        var newIndex = ((currentIndex + offset) % FilteredCommands.Count + FilteredCommands.Count) % FilteredCommands.Count;
        SelectedCommand = FilteredCommands[newIndex];
    }

    private void ApplyFilter()
    {
        var searchWords = SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        FilteredCommands.Clear();
        foreach (var command in _commands.Where(command => searchWords.All(searchWord => _comparer.IndexOf(command.Label, searchWord, SearchOptions) >= 0)))
            FilteredCommands.Add(command);
        SelectedCommand = FilteredCommands.FirstOrDefault();
    }
}
