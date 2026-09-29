using System.IO;
using DevLauncher.Models;

namespace DevLauncher.Services;

/// <summary>
/// Lists the projects of the configured projects root folder and detects their technologies.
/// </summary>
public class ProjectScanner
{
    /// <summary>
    /// Returns the full paths of the sub-folders of the projects root folder, sorted alphabetically.
    /// Hidden folders and dot-folders are ignored.
    /// </summary>
    public List<string> GetProjects()
    {
        var projectsRootPath = AppSettings.HtdocsPath;
        if (!Directory.Exists(projectsRootPath)) return new List<string>();
        try
        {
            return new DirectoryInfo(projectsRootPath)
                .EnumerateDirectories()
                .Where(directory => !directory.Name.StartsWith('.') && !directory.Attributes.HasFlag(FileAttributes.Hidden))
                .Select(directory => directory.FullName)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// Detects the technologies used by a project.
    /// Symfony criteria : symfony.lock, bin/console, or composer.json requiring symfony/framework-bundle.
    /// </summary>
    public ProjectDetection DetectProject(string projectPath)
    {
        var composerJsonContent = ReadComposerJson(projectPath);
        var isSymfony = File.Exists(Path.Combine(projectPath, "symfony.lock"))
            || File.Exists(Path.Combine(projectPath, "bin", "console"))
            || composerJsonContent.Contains("symfony/framework-bundle", StringComparison.OrdinalIgnoreCase);
        var usesTailwindBundle = composerJsonContent.Contains("symfonycasts/tailwind-bundle", StringComparison.OrdinalIgnoreCase);
        return new ProjectDetection(isSymfony, usesTailwindBundle);
    }

    /// <summary>Returns the content of composer.json, or an empty string when it is missing or unreadable.</summary>
    private static string ReadComposerJson(string projectPath)
    {
        var composerJsonPath = Path.Combine(projectPath, "composer.json");
        try
        {
            return File.Exists(composerJsonPath) ? File.ReadAllText(composerJsonPath) : string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}
