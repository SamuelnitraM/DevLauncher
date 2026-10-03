using System.IO;
using DevLauncher.Models;
using DevLauncher.Services;
using Xunit;

namespace DevLauncher.Tests;

public class FavoriteProjectsServiceTests
{
    [Fact]
    public void FavoritesAreToggledAndKeptInPinningOrder()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var favoriteProjectsService = new FavoriteProjectsService(temporaryDirectory.Combine("data", "favorite-projects.json"));
        Assert.Empty(favoriteProjectsService.GetFavoriteProjectPaths());
        Assert.True(favoriteProjectsService.ToggleFavorite(@"C:\xampp\htdocs\site"));
        Assert.True(favoriteProjectsService.ToggleFavorite(@"D:\dev\api"));
        Assert.True(favoriteProjectsService.IsFavorite(@"C:\XAMPP\htdocs\site" + Path.DirectorySeparatorChar));
        Assert.False(favoriteProjectsService.ToggleFavorite(@"c:\xampp\htdocs\SITE"));
        Assert.Equal(new[] { @"D:\dev\api" }, favoriteProjectsService.GetFavoriteProjectPaths());
    }

    [Fact]
    public void CorruptedFavoritesGiveAnEmptyList()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        File.WriteAllText(temporaryDirectory.Combine("favorite-projects.json"), "{");
        Assert.Empty(new FavoriteProjectsService(temporaryDirectory.Combine("favorite-projects.json")).GetFavoriteProjectPaths());
    }
}

public class GitStatusTests
{
    [Fact]
    public void PorcelainStatusIsParsed()
    {
        const string statusOutput = """
            # branch.oid 1a2b3c4d5e6f7a8b9c0d
            # branch.head feature/login
            # branch.upstream origin/feature/login
            # branch.ab +2 -5
            1 .M N... 100644 100644 100644 abc abc src/Controller.php
            1 A. N... 000000 100644 100644 000 def templates/login.html.twig
            ? notes.txt
            """;
        var gitStatus = GitStatusService.ParsePorcelainStatus(statusOutput.Replace("\r", string.Empty));
        Assert.Equal(new GitStatus("feature/login", 3, 2, 5), gitStatus);
        Assert.Equal("🌿 feature/login  ·  ✏️ 3 modification(s)  ·  ⬆️ 2 à pousser  ·  ⬇️ 5 en retard", gitStatus.Summary);
    }

    [Fact]
    public void CleanDetachedHeadWithoutUpstream()
    {
        var gitStatus = GitStatusService.ParsePorcelainStatus("# branch.oid 1a2b3c4d5e6f\n# branch.head (detached)\n");
        Assert.Equal("détachée (1a2b3c4)", gitStatus.Branch);
        Assert.Null(gitStatus.AheadCount);
        Assert.Equal("🌿 détachée (1a2b3c4)  ·  ✔ à jour", gitStatus.Summary);
    }

    [Fact]
    public void BranchIsReadFromHeadFileWithoutGit()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Directory.CreateDirectory(temporaryDirectory.Combine(".git"));
        File.WriteAllText(temporaryDirectory.Combine(".git", "HEAD"), "ref: refs/heads/main\n");
        Assert.True(GitStatusService.IsGitRepository(temporaryDirectory.DirectoryPath));
        var gitStatus = GitStatusService.ReadBranchFromHeadFile(temporaryDirectory.DirectoryPath);
        Assert.Equal("main", gitStatus?.Branch);
        Assert.Equal("🌿 main", gitStatus?.Summary);
    }

    [Fact]
    public async Task FolderWithoutRepositoryHasNoStatus()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Assert.Null(await new GitStatusService().GetStatusAsync(temporaryDirectory.DirectoryPath));
    }
}

public class ProjectContextBuilderTests
{
    [Fact]
    public void ContextDescribesStackCommandsAndTree()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var projectPath = temporaryDirectory.Combine("boutique");
        Directory.CreateDirectory(Path.Combine(projectPath, "app", "Http"));
        Directory.CreateDirectory(Path.Combine(projectPath, "vendor", "laravel"));
        Directory.CreateDirectory(Path.Combine(projectPath, "node_modules", "vite"));
        File.WriteAllText(Path.Combine(projectPath, "artisan"), "");
        File.WriteAllText(Path.Combine(projectPath, "compose.yaml"), "");
        File.WriteAllText(Path.Combine(projectPath, "app", "Http", "Kernel.php"), "");
        var projectContext = ProjectContextBuilder.Build(new ProjectContextBuilder.ProjectContextInput(
            projectPath, ProjectType.Laravel, false, new[] { "dev", "build" }, "http://127.0.0.1:8000", new GitStatus("main", 2, 0, 0)));
        Assert.Contains("# Projet boutique", projectContext);
        Assert.Contains("- Stack : Laravel, npm, Docker Compose", projectContext);
        Assert.Contains("- URL locale : http://127.0.0.1:8000", projectContext);
        Assert.Contains("- Git : branche `main`, 2 fichier(s) modifié(s)", projectContext);
        Assert.Contains("- `php artisan serve`", projectContext);
        Assert.Contains("- `npm run build`", projectContext);
        Assert.Contains("├── app/", projectContext);
        Assert.Contains("│   ├── Http/", projectContext);
        Assert.Contains("├── artisan", projectContext);
        Assert.DoesNotContain("vendor", projectContext);
        Assert.DoesNotContain("node_modules", projectContext);
        Assert.DoesNotContain("Kernel.php", projectContext);
    }
}

public class ProjectUrlResolverTests
{
    [Fact]
    public void KnownUrlFollowsTheProjectType()
    {
        Assert.Equal($"http://127.0.0.1:{AppSettings.SymfonyPort}", ProjectUrlResolver.GetKnownUrl(@"C:\dev\site", ProjectType.Symfony, null));
        Assert.Equal("http://localhost:5173/", ProjectUrlResolver.GetKnownUrl(@"C:\dev\front", ProjectType.Node, "http://localhost:5173/"));
        Assert.Null(ProjectUrlResolver.GetKnownUrl(@"C:\dev\front", ProjectType.Node, null));
    }
}
