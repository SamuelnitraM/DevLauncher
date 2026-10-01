using System.IO;
using DevLauncher.Models;
using DevLauncher.Services;
using Xunit;

namespace DevLauncher.Tests;

public class ProjectScannerTests
{
    private static string CreateProject(TemporaryDirectory temporaryDirectory, string projectName, params (string RelativePath, string Content)[] projectFiles)
    {
        var projectPath = temporaryDirectory.Combine(projectName);
        Directory.CreateDirectory(projectPath);
        foreach (var (relativePath, content) in projectFiles)
        {
            var filePath = Path.Combine(projectPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, content);
        }
        return projectPath;
    }

    [Theory]
    [InlineData(ProjectType.Laravel, "artisan", "")]
    [InlineData(ProjectType.Laravel, "composer.json", """{"require":{"laravel/framework":"^11"}}""")]
    [InlineData(ProjectType.Symfony, "symfony.lock", "{}")]
    [InlineData(ProjectType.Symfony, "bin/console", "")]
    [InlineData(ProjectType.WordPress, "wp-config.php", "<?php")]
    [InlineData(ProjectType.Django, "manage.py", "")]
    [InlineData(ProjectType.DotNet, "src/Api/Api.csproj", """<Project Sdk="Microsoft.NET.Sdk.Web"></Project>""")]
    [InlineData(ProjectType.Node, "package.json", """{"scripts":{"dev":"vite"}}""")]
    [InlineData(ProjectType.Other, "composer.json", """{"require":{"monolog/monolog":"*"}}""")]
    [InlineData(ProjectType.Other, "index.php", "<?php")]
    public void FrameworkIsDetectedFromItsMarkers(ProjectType expectedProjectType, string markerFile, string markerContent)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var projectPath = CreateProject(temporaryDirectory, "projet", (markerFile, markerContent));
        Assert.Equal(expectedProjectType, new ProjectScanner().DetectProject(projectPath).ProjectType);
    }

    [Fact]
    public void LaravelWithViteHasNpmScripts()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var projectPath = CreateProject(temporaryDirectory, "boutique",
            ("artisan", ""),
            ("composer.json", """{"require":{"laravel/framework":"^11"}}"""),
            ("package.json", """{ "scripts": { "dev": "vite", "build": "vite build", }, }"""));
        var projectDetection = new ProjectScanner().DetectProject(projectPath);
        Assert.Equal(ProjectType.Laravel, projectDetection.ProjectType);
        Assert.True(projectDetection.HasPackageJson);
        Assert.Equal(new[] { "dev", "build" }, ProjectScanner.GetNpmScripts(projectPath));
    }

    [Fact]
    public void ProjectsComeFromSeveralRootsAndAddedFolders()
    {
        using var firstRoot = new TemporaryDirectory();
        using var secondRoot = new TemporaryDirectory();
        using var elsewhere = new TemporaryDirectory();
        Directory.CreateDirectory(firstRoot.Combine("site"));
        Directory.CreateDirectory(firstRoot.Combine("dashboard"));
        Directory.CreateDirectory(firstRoot.Combine(".git"));
        Directory.CreateDirectory(secondRoot.Combine("api"));
        Directory.CreateDirectory(secondRoot.Combine("site"));
        var addedProjectPath = elsewhere.Combine("outil");
        Directory.CreateDirectory(addedProjectPath);
        var projectPaths = ProjectScanner.GetProjects(
            new[] { firstRoot.DirectoryPath, secondRoot.DirectoryPath, firstRoot.Combine("missing") },
            new[] { addedProjectPath, secondRoot.Combine("api"), elsewhere.Combine("missing") },
            new[] { "Dashboard" });
        Assert.Equal(new[] { "api", "outil", "site", "site" }, projectPaths.Select(Path.GetFileName));
        Assert.Contains(addedProjectPath, projectPaths);
    }
}
