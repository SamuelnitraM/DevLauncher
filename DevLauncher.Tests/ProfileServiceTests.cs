using System.IO;
using DevLauncher.Models;
using DevLauncher.Services;
using Xunit;

namespace DevLauncher.Tests;

public class ProfileServiceTests
{
    private const string LegacyProfilesJson = """
        [
          {"name":"Défaut","isSymfony":true,"openVSCode":true,"openVisualStudio":false,"showXamppPanel":false,"startApache":false,
           "startMySQL":true,"startFileZilla":false,"startSymfonyServer":true,"startTailwind":true,"startMercure":true,
           "mercureScript":"start-dev.ps1","openTerminal":false,"openBrowser":true,"browserDefault":false,"browserChrome":true,"browserFirefox":true},
          {"name":"Front","isSymfony":false,"startApache":true}
        ]
        """;

    [Fact]
    public void LegacyProfilesAreConvertedAndRewritten()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        File.WriteAllText(temporaryDirectory.Combine("highlightforge.json"), LegacyProfilesJson);
        var profileService = new ProfileService(temporaryDirectory.DirectoryPath);
        var profiles = profileService.GetProfiles("highlightforge");
        Assert.Equal(new[] { "Défaut", "Front" }, profiles.Select(profile => profile.Name));
        var symfonyProfile = profiles[0];
        Assert.Equal(ProjectType.Symfony, symfonyProfile.ProjectType);
        Assert.True(symfonyProfile.IsToolEnabled(ToolIds.VSCode));
        Assert.True(symfonyProfile.IsToolEnabled(ToolIds.SymfonyServer));
        Assert.True(symfonyProfile.IsToolEnabled(ToolIds.Tailwind));
        Assert.False(symfonyProfile.IsToolEnabled(ToolIds.Apache));
        Assert.Equal(new[] { "start-dev.ps1" }, symfonyProfile.GetToolSelection(ToolIds.Mercure).GetOptionValues(ToolIds.MercureScriptOption));
        Assert.Equal(new[] { ToolIds.ChromeBrowser, ToolIds.FirefoxBrowser }, symfonyProfile.GetToolSelection(ToolIds.Browser).GetOptionValues(ToolIds.BrowserTargetsOption));
        Assert.Equal(ProjectType.Other, profiles[1].ProjectType);
        Assert.True(profiles[1].IsToolEnabled(ToolIds.Apache));
        var rewrittenJson = File.ReadAllText(temporaryDirectory.Combine("highlightforge.json"));
        Assert.Contains("\"tools\"", rewrittenJson);
        Assert.Contains("Défaut", rewrittenJson);
    }

    [Fact]
    public void RenameKeepsPositionAndLastUsedProfile()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var profileService = new ProfileService(temporaryDirectory.DirectoryPath);
        profileService.SaveProfile("site", new ProjectProfile { Name = "A" });
        profileService.SaveProfile("site", new ProjectProfile { Name = "B" });
        profileService.SaveProfile("site", new ProjectProfile { Name = "C" });
        profileService.SaveLastUsedProfile("site", "B");
        profileService.RenameProfile("site", "B", "B2");
        Assert.Equal(new[] { "A", "B2", "C" }, profileService.GetProfiles("site").Select(profile => profile.Name));
        Assert.Equal("B2", profileService.GetLastUsedProfile("site"));
    }

    [Fact]
    public void DefaultProfileFollowsTheDetection()
    {
        var symfonyProfile = ProjectProfile.CreateDefault(new ProjectDetection(IsSymfony: true, UsesTailwindBundle: true));
        Assert.Equal(ProjectType.Symfony, symfonyProfile.ProjectType);
        Assert.True(symfonyProfile.IsToolEnabled(ToolIds.SymfonyServer));
        Assert.True(symfonyProfile.IsToolEnabled(ToolIds.Tailwind));
        Assert.False(symfonyProfile.IsToolEnabled(ToolIds.Apache));
        var otherProfile = ProjectProfile.CreateDefault(new ProjectDetection(IsSymfony: false, UsesTailwindBundle: false));
        Assert.True(otherProfile.IsToolEnabled(ToolIds.Apache));
        Assert.False(otherProfile.IsToolEnabled(ToolIds.SymfonyServer));
    }

    [Fact]
    public void CorruptedFileGivesNoProfile()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        File.WriteAllText(temporaryDirectory.Combine("broken.json"), "{ not json");
        Assert.Empty(new ProfileService(temporaryDirectory.DirectoryPath).GetProfiles("broken"));
    }
}
