using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DevLauncher.Models;
using DevLauncher.Services.Tools;

namespace DevLauncher.ViewModels;

/// <summary>
/// Tool shown in its category card : a check box, or a radio button in an exclusive category.
/// The "no tool" choice of an exclusive category is a tool view model without tool.
/// </summary>
public partial class ToolViewModel : ObservableObject
{
    private readonly ToolCategoryViewModel _categoryViewModel;

    public ToolViewModel(ToolCategoryViewModel categoryViewModel, LaunchTool? tool, string label)
    {
        _categoryViewModel = categoryViewModel;
        Tool = tool;
        Label = label;
        foreach (var optionDefinition in tool?.Options ?? Array.Empty<ToolOptionDefinition>())
            Options.Add(new ToolOptionViewModel(optionDefinition));
    }

    public LaunchTool? Tool { get; }
    public string Label { get; }
    public bool IsExclusive => _categoryViewModel.IsExclusive;
    public ObservableCollection<ToolOptionViewModel> Options { get; } = new();
    public bool ShowOptions => IsEnabled && IsAvailable && Options.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOptions))]
    private bool _isEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOptions))]
    private bool _isAvailable = true;

    partial void OnIsEnabledChanged(bool value) => _categoryViewModel.OnToolEnabledChanged(this);

    public void ApplySelection(ToolSelection toolSelection)
    {
        IsEnabled = toolSelection.IsEnabled;
        foreach (var option in Options) option.ApplyValues(toolSelection.GetOptionValues(option.Key));
    }

    public ToolSelection CaptureSelection() => new()
    {
        IsEnabled = IsEnabled,
        Options = Options.ToDictionary(option => option.Key, option => option.CaptureValues()),
    };

    public void ReloadOptionChoices(ToolOptionContext optionContext)
    {
        foreach (var option in Options) option.ReloadChoices(optionContext);
    }
}
