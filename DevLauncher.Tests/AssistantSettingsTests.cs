using DevLauncher.Models;
using DevLauncher.Services.Assistants;
using DevLauncher.Views;
using Xunit;

namespace DevLauncher.Tests;

public class AssistantSettingsTests
{
    [Fact]
    public void SavedSettingsAreCompletedAndCleaned()
    {
        var savedSettings = new[]
        {
            new AssistantSettings
            {
                Id = AssistantCatalog.ClaudeId,
                IsEnabled = false,
                DefaultMode = "unknown",
                Projects = new List<AssistantProject>
                {
                    new() { Name = "", Url = "https://claude.ai/project/1" },
                    new() { Name = "highlightforge", Url = "https://claude.ai/project/2" },
                },
            },
        };
        var mergedSettings = AssistantCatalog.MergeWithDefaults(savedSettings);
        Assert.Equal(AssistantCatalog.Definitions.Select(definition => definition.Id), mergedSettings.Select(settings => settings.Id));
        var claudeSettings = mergedSettings.Single(settings => settings.Id == AssistantCatalog.ClaudeId);
        Assert.False(claudeSettings.IsEnabled);
        Assert.Equal(AssistantModes.Browser, claudeSettings.DefaultMode);
        Assert.Equal("https://claude.ai/new", claudeSettings.WebUrl);
        Assert.Equal("highlightforge", Assert.Single(claudeSettings.Projects).Name);
        Assert.True(mergedSettings.Single(settings => settings.Id == AssistantCatalog.ClaudeCodeId).IsEnabled);
    }

    [Fact]
    public void ProjectLinesAreParsedFromTheSettingsWindow()
    {
        var settingsRow = new AssistantSettingsRow(AssistantCatalog.GetDefinition(AssistantCatalog.ClaudeId), new AssistantSettings { Id = AssistantCatalog.ClaudeId })
        {
            ProjectsText = "highlightforge | https://claude.ai/project/abc\r\nsans url\r\n  templateSite|https://claude.ai/project/def  \r\nmauvais | ftp://x\r\n",
            OpensInApplication = true,
        };
        var editedSettings = settingsRow.ToSettings();
        Assert.Equal(AssistantModes.Application, editedSettings.DefaultMode);
        Assert.Equal(new[] { "highlightforge", "templateSite" }, editedSettings.Projects.Select(project => project.Name));
        Assert.Equal("https://claude.ai/project/def", editedSettings.Projects[1].Url);
    }
}
