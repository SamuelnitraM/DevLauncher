using CommunityToolkit.Mvvm.ComponentModel;
using DevLauncher.Services.Assistants;

namespace DevLauncher.Views;

/// <summary>Editable settings of one command line agent in the settings window, with its installation state.</summary>
public partial class CliAgentSettingsRow : ObservableObject
{
    public CliAgentSettingsRow(CliAgentDefinition agentDefinition, bool isEnabled)
    {
        AgentDefinition = agentDefinition;
        _isEnabled = isEnabled;
        var installedCommandPath = CliAgentCatalog.FindInstalledCommand(agentDefinition);
        IsInstalled = installedCommandPath is not null;
        InstallationHint = IsInstalled
            ? $"✅ Installé : {installedCommandPath}"
            : $"❌ Commande « {agentDefinition.Command} » introuvable dans le PATH";
    }

    public CliAgentDefinition AgentDefinition { get; }
    public string Label => $"{AgentDefinition.Icon} {AgentDefinition.DisplayName}";
    public bool IsInstalled { get; }
    public string InstallationHint { get; }

    [ObservableProperty]
    private bool _isEnabled;

    public CliAgentSettings ToSettings() => new() { Id = AgentDefinition.Id, IsEnabled = IsEnabled };
}

/// <summary>Family of applications that can start without the administrator rights, as a check box of the settings window.</summary>
public sealed class UnelevatedFamilyRow
{
    public required string Family { get; init; }
    public required string Label { get; init; }
    public bool IsSelected { get; set; }
}
