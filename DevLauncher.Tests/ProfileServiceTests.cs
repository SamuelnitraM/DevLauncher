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
        var profiles = profileService.GetProfiles(temporaryDirectory.Combine("projects", "highlightforge"));
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
        var projectPath = temporaryDirectory.Combine("projects", "site");
        profileService.SaveProfile(projectPath, new ProjectProfile { Name = "A" });
        profileService.SaveProfile(projectPath, new ProjectProfile { Name = "B" });
        profileService.SaveProfile(projectPath, new ProjectProfile { Name = "C" });
        profileService.SaveLastUsedProfile(projectPath, "B");
        profileService.RenameProfile(projectPath, "B", "B2");
        Assert.Equal(new[] { "A", "B2", "C" }, profileService.GetProfiles(projectPath).Select(profile => profile.Name));
        Assert.Equal("B2", profileService.GetLastUsedProfile(projectPath));
    }

    [Fact]
    public void DefaultProfileFollowsTheDetection()
    {
        var symfonyProfile = ProjectProfile.CreateDefault(new ProjectDetection(ProjectType.Symfony, UsesTailwindBundle: true, HasPackageJson: false));
        Assert.Equal(ProjectType.Symfony, symfonyProfile.ProjectType);
        Assert.True(symfonyProfile.IsToolEnabled(ToolIds.SymfonyServer));
        Assert.True(symfonyProfile.IsToolEnabled(ToolIds.Tailwind));
        Assert.False(symfonyProfile.IsToolEnabled(ToolIds.Apache));
        var laravelProfile = ProjectProfile.CreateDefault(new ProjectDetection(ProjectType.Laravel, UsesTailwindBundle: false, HasPackageJson: true));
        Assert.True(laravelProfile.IsToolEnabled(ToolIds.LaravelServer));
        Assert.True(laravelProfile.IsToolEnabled(ToolIds.NpmScript));
        Assert.True(laravelProfile.IsToolEnabled(ToolIds.MySql));
        var nodeProfile = ProjectProfile.CreateDefault(new ProjectDetection(ProjectType.Node, UsesTailwindBundle: false, HasPackageJson: true));
        Assert.True(nodeProfile.IsToolEnabled(ToolIds.NpmScript));
        Assert.False(nodeProfile.IsToolEnabled(ToolIds.Apache));
        var otherProfile = ProjectProfile.CreateDefault(new ProjectDetection(ProjectType.Other, UsesTailwindBundle: false, HasPackageJson: false));
        Assert.True(otherProfile.IsToolEnabled(ToolIds.Apache));
        Assert.False(otherProfile.IsToolEnabled(ToolIds.SymfonyServer));
    }

    [Fact]
    public void CorruptedFileGivesNoProfile()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        File.WriteAllText(temporaryDirectory.Combine("broken.json"), "{ not json");
        Assert.Empty(new ProfileService(temporaryDirectory.DirectoryPath).GetProfiles(temporaryDirectory.Combine("projects", "broken")));
    }

    [Fact]
    public void SharedProfilesAreReadAndWrittenInTheProject()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var profileService = new ProfileService(temporaryDirectory.Combine("data"));
        var projectPath = temporaryDirectory.Combine("site");
        Directory.CreateDirectory(projectPath);
        profileService.SaveProfile(projectPath, new ProjectProfile { Name = "Local", ProjectType = ProjectType.Laravel });
        Assert.False(ProfileService.IsSharedInProject(projectPath));
        profileService.ShareInProject(projectPath);
        Assert.True(ProfileService.IsSharedInProject(projectPath));
        var sharedJson = File.ReadAllText(Path.Combine(projectPath, ProfileService.ProjectProfilesFileName));
        Assert.Contains("\"version\": 1", sharedJson);
        Assert.Contains("\"Laravel\"", sharedJson);
        profileService.SaveProfile(projectPath, new ProjectProfile { Name = "Équipe" });
        Assert.Equal(new[] { "Local", "Équipe" }, profileService.GetProfiles(projectPath).Select(profile => profile.Name));
        Assert.Contains("Équipe", File.ReadAllText(Path.Combine(projectPath, ProfileService.ProjectProfilesFileName)));
        profileService.SaveLastUsedProfile(projectPath, "Équipe");
        Assert.DoesNotContain("Équipe", File.ReadAllText(temporaryDirectory.Combine("data", "site.json")));
        profileService.StopSharingInProject(projectPath);
        Assert.False(ProfileService.IsSharedInProject(projectPath));
        Assert.Equal(new[] { "Local", "Équipe" }, profileService.GetProfiles(projectPath).Select(profile => profile.Name));
        Assert.Equal("Équipe", profileService.GetLastUsedProfile(projectPath));
    }

    [Fact]
    public void SharedFileAcceptsAPlainArrayAndComments()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var projectPath = temporaryDirectory.Combine("site");
        Directory.CreateDirectory(projectPath);
        File.WriteAllText(Path.Combine(projectPath, ProfileService.ProjectProfilesFileName), """
            // Profils de l'équipe
            [ { "name": "Front", "projectType": "Node", "tools": { "npm-script": { "isEnabled": true } } }, ]
            """);
        var profile = Assert.Single(new ProfileService(temporaryDirectory.Combine("data")).GetProfiles(projectPath));
        Assert.Equal(ProjectType.Node, profile.ProjectType);
        Assert.True(profile.IsToolEnabled(ToolIds.NpmScript));
    }

    [Fact]
    public void BrokenSharedFileIsNeverOverwritten()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var projectPath = temporaryDirectory.Combine("site");
        Directory.CreateDirectory(projectPath);
        var sharedFilePath = Path.Combine(projectPath, ProfileService.ProjectProfilesFileName);
        File.WriteAllText(sharedFilePath, "{ conflict <<<<<<< HEAD");
        var profileService = new ProfileService(temporaryDirectory.Combine("data"));
        Assert.Throws<InvalidDataException>(() => profileService.GetProfiles(projectPath));
        Assert.Throws<InvalidDataException>(() => profileService.SaveProfile(projectPath, new ProjectProfile { Name = "X" }));
        Assert.Equal("{ conflict <<<<<<< HEAD", File.ReadAllText(sharedFilePath));
    }
}
