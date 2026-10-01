using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DevLauncher.Services.Tools;

namespace DevLauncher.ViewModels;

public partial class ToolOptionChoiceViewModel : ObservableObject
{
    public ToolOptionChoiceViewModel(ToolOptionChoice choice)
    {
        Value = choice.Value;
        Label = choice.Label;
    }

    public string Value { get; }
    public string Label { get; }

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Name read by the accessibility tools and the UI automation.</summary>
    public override string ToString() => Label;
}

/// <summary>Option of a tool : a dropdown for a single choice, check boxes for multiple choices.</summary>
public partial class ToolOptionViewModel : ObservableObject
{
    private readonly ToolOptionDefinition _definition;
    private ToolOptionContext _optionContext = new(null);

    public ToolOptionViewModel(ToolOptionDefinition definition)
    {
        _definition = definition;
        ReloadChoices(_optionContext);
    }

    public string Key => _definition.Key;
    public string Label => _definition.Label;
    public bool IsSingleChoice => _definition.Kind == ToolOptionKind.SingleChoice;
    public bool IsMultipleChoice => _definition.Kind == ToolOptionKind.MultipleChoice;
    public bool HasNoChoices => Choices.Count == 0;

    public ObservableCollection<ToolOptionChoiceViewModel> Choices { get; } = new();

    [ObservableProperty]
    private ToolOptionChoiceViewModel? _selectedChoice;

    /// <summary>Reads the choices again (they can depend on the project and the settings) and keeps the selected values that still exist.</summary>
    public void ReloadChoices(ToolOptionContext optionContext)
    {
        _optionContext = optionContext;
        var selectedValues = CaptureValues();
        Choices.Clear();
        foreach (var choice in _definition.GetChoices(optionContext)) Choices.Add(new ToolOptionChoiceViewModel(choice));
        ApplyValues(selectedValues);
        OnPropertyChanged(nameof(HasNoChoices));
    }

    /// <summary>Selects the given values, or the default values when none is given.</summary>
    public void ApplyValues(IReadOnlyList<string> values)
    {
        var effectiveValues = values.Count > 0 ? values : _definition.GetDefaultValues(_optionContext);
        if (IsSingleChoice)
        {
            SelectedChoice = Choices.FirstOrDefault(choice => effectiveValues.Contains(choice.Value)) ?? Choices.FirstOrDefault();
            return;
        }
        foreach (var choice in Choices) choice.IsSelected = effectiveValues.Contains(choice.Value);
    }

    public List<string> CaptureValues()
    {
        if (IsSingleChoice) return SelectedChoice is null ? new List<string>() : new List<string> { SelectedChoice.Value };
        return Choices.Where(choice => choice.IsSelected).Select(choice => choice.Value).ToList();
    }
}
