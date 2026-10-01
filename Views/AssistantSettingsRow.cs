using CommunityToolkit.Mvvm.ComponentModel;
using DevLauncher.Models;
using DevLauncher.Services.Assistants;

namespace DevLauncher.Views;

/// <summary>Editable settings of one assistant in the settings window.</summary>
public partial class AssistantSettingsRow : ObservableObject
{
    private const char ProjectFieldSeparator = '|';

    private readonly AssistantDefinition _assistantDefinition;

    public AssistantSettingsRow(AssistantDefinition assistantDefinition, AssistantSettings assistantSettings)
    {
        _assistantDefinition = assistantDefinition;
        _isEnabled = assistantSettings.IsEnabled;
        OpensInApplication = assistantSettings.DefaultMode == AssistantModes.Application;
        ApplicationTarget = assistantSettings.ApplicationTarget;
        WebUrl = assistantSettings.WebUrl;
        ProjectsText = string.Join(Environment.NewLine, assistantSettings.Projects.Select(project => $"{project.Name} {ProjectFieldSeparator} {project.Url}"));
    }

    public string Label => $"{_assistantDefinition.Icon} {_assistantDefinition.DisplayName}";
    public bool IsChat => _assistantDefinition.Kind == AssistantKind.Chat;

    [ObservableProperty]
    private bool _isEnabled;

    public bool OpensInApplication { get; set; }
    public string ApplicationTarget { get; set; }
    public string WebUrl { get; set; }
    public string ProjectsText { get; set; }

    /// <summary>Application found automatically, used when no target is typed.</summary>
    public string ApplicationHint
    {
        get
        {
            var detectedTarget = AssistantCatalog.DetectApplicationTarget(_assistantDefinition);
            return detectedTarget is null
                ? "Application non détectée : renseigne sa cible pour le mode Application."
                : $"Détectée automatiquement : {detectedTarget}";
        }
    }

    /// <summary>Builds the settings to save. Project lines without a name and an http(s) URL are ignored.</summary>
    public AssistantSettings ToSettings() => new()
    {
        Id = _assistantDefinition.Id,
        IsEnabled = IsEnabled,
        DefaultMode = OpensInApplication ? AssistantModes.Application : AssistantModes.Browser,
        ApplicationTarget = ApplicationTarget.Trim(),
        WebUrl = string.IsNullOrWhiteSpace(WebUrl) ? _assistantDefinition.DefaultWebUrl : WebUrl.Trim(),
        Projects = ProjectsText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(projectLine => projectLine.Split(ProjectFieldSeparator, 2, StringSplitOptions.TrimEntries))
            .Where(projectFields => projectFields.Length == 2
                && projectFields[0].Length > 0
                && Uri.TryCreate(projectFields[1], UriKind.Absolute, out var projectUri)
                && (projectUri.Scheme == Uri.UriSchemeHttps || projectUri.Scheme == Uri.UriSchemeHttp))
            .Select(projectFields => new AssistantProject { Name = projectFields[0], Url = projectFields[1] })
            .ToList(),
    };
}
