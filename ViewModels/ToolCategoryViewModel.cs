using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DevLauncher.Models;
using DevLauncher.Services.Tools;

namespace DevLauncher.ViewModels;

/// <summary>Card of tools. In an exclusive category, enabling a tool disables the others.</summary>
public partial class ToolCategoryViewModel : ObservableObject
{
    private readonly ToolViewModel? _noToolChoice;

    public ToolCategoryViewModel(ToolCategory category, IEnumerable<LaunchTool> tools)
    {
        Title = category.Title;
        IsExclusive = category.IsExclusive;
        if (category.IsExclusive)
        {
            _noToolChoice = new ToolViewModel(this, null, category.NoneChoiceLabel ?? "Aucun");
            Tools.Add(_noToolChoice);
        }
        foreach (var tool in tools) Tools.Add(new ToolViewModel(this, tool, tool.Label));
    }

    public string Title { get; }
    public bool IsExclusive { get; }
    public ObservableCollection<ToolViewModel> Tools { get; } = new();

    public IEnumerable<ToolViewModel> RealTools => Tools.Where(toolViewModel => toolViewModel.Tool is not null);

    [ObservableProperty]
    private bool _isVisible = true;

    /// <summary>Shows only the tools enabled in the settings and supporting the project type, and hides the card when none does.</summary>
    public void UpdateAvailability(ProjectType projectType)
    {
        foreach (var toolViewModel in RealTools) toolViewModel.IsAvailable = toolViewModel.Tool!.Supports(projectType);
        IsVisible = RealTools.Any(toolViewModel => toolViewModel.IsAvailable);
    }

    public void ApplyProfile(ProjectProfile profile)
    {
        foreach (var toolViewModel in RealTools) toolViewModel.ApplySelection(profile.GetToolSelection(toolViewModel.Tool!.Id));
        if (_noToolChoice is not null && !RealTools.Any(toolViewModel => toolViewModel.IsEnabled)) _noToolChoice.IsEnabled = true;
    }

    public void CaptureInto(ProjectProfile profile)
    {
        foreach (var toolViewModel in RealTools) profile.Tools[toolViewModel.Tool!.Id] = toolViewModel.CaptureSelection();
    }

    public void ReloadOptionChoices(ToolOptionContext optionContext)
    {
        foreach (var toolViewModel in RealTools) toolViewModel.ReloadOptionChoices(optionContext);
    }

    internal void OnToolEnabledChanged(ToolViewModel changedToolViewModel)
    {
        if (!IsExclusive || !changedToolViewModel.IsEnabled) return;
        foreach (var otherToolViewModel in Tools.Where(toolViewModel => toolViewModel != changedToolViewModel))
            otherToolViewModel.IsEnabled = false;
    }
}
