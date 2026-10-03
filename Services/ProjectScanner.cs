using System.IO;
using System.Text.Json;
using DevLauncher.Models;

namespace DevLauncher.Services;

/// <summary>
/// Lists the projects of the configured folders and detects their technologies.
/// </summary>
public class ProjectScanner
{
    private const int DotNetProjectSearchDepth = 2;

    /// <summary>Lists the projects of the folders configured in the settings.</summary>
    public List<string> GetProjects() => GetProjects(AppSettings.ProjectRoots, AppSettings.ExtraProjectPaths, AppSettings.ExcludedFolderNames);

    /// <summary>
    /// Returns the full paths of the sub-folders of the project roots and of the projects added one by one,
    /// without duplicates, sorted by name. Hidden folders, dot-folders and excluded names are ignored.
    /// </summary>
    public static List<string> GetProjects(IEnumerable<string> projectRoots, IEnumerable<string> extraProjectPaths, IEnumerable<string> excludedFolderNames)
    {
        var excludedNames = excludedFolderNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rootProjectPaths = projectRoots.Where(Directory.Exists).SelectMany(projectRoot => EnumerateProjectFolders(projectRoot, excludedNames));
        var addedProjectPaths = extraProjectPaths.Where(Directory.Exists).Select(Path.GetFullPath);
        return rootProjectPaths
            .Concat(addedProjectPaths)
            .Select(Path.TrimEndingDirectorySeparator)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> EnumerateProjectFolders(string projectRoot, IReadOnlySet<string> excludedNames)
    {
        try
        {
            return new DirectoryInfo(projectRoot)
                .EnumerateDirectories()
                .Where(directory => !directory.Name.StartsWith('.')
                    && !directory.Attributes.HasFlag(FileAttributes.Hidden)
                    && !excludedNames.Contains(directory.Name))
                .Select(directory => directory.FullName)
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Detects the framework of a project, from the most specific markers to the most generic ones :
    /// Laravel (artisan), Symfony (symfony.lock, bin/console, framework-bundle), WordPress (wp-config.php, wp-content),
    /// Django (manage.py), .NET (a .csproj), Node.js (package.json without composer.json).
    /// </summary>
    public ProjectDetection DetectProject(string projectPath)
    {
        var composerJsonContent = ReadTextFile(Path.Combine(projectPath, "composer.json"));
        var hasPackageJson = File.Exists(Path.Combine(projectPath, "package.json"));
        var usesTailwindBundle = composerJsonContent.Contains("symfonycasts/tailwind-bundle", StringComparison.OrdinalIgnoreCase);
        return new ProjectDetection(DetectProjectType(projectPath, composerJsonContent, hasPackageJson), usesTailwindBundle, hasPackageJson,
            FindComposeFile(projectPath) is not null);
    }

    private static ProjectType DetectProjectType(string projectPath, string composerJsonContent, bool hasPackageJson)
    {
        bool HasFile(params string[] relativePathParts) => File.Exists(Path.Combine(new[] { projectPath }.Concat(relativePathParts).ToArray()));
        bool RequiresComposerPackage(string packageName) => composerJsonContent.Contains(packageName, StringComparison.OrdinalIgnoreCase);
        if (HasFile("artisan") || RequiresComposerPackage("laravel/framework")) return ProjectType.Laravel;
        if (HasFile("symfony.lock") || HasFile("bin", "console") || RequiresComposerPackage("symfony/framework-bundle")) return ProjectType.Symfony;
        if (HasFile("wp-config.php") || HasFile("wp-config-sample.php") || Directory.Exists(Path.Combine(projectPath, "wp-content"))) return ProjectType.WordPress;
        if (HasFile("manage.py")) return ProjectType.Django;
        if (FindDotNetProject(projectPath) is not null) return ProjectType.DotNet;
        if (hasPackageJson && composerJsonContent.Length == 0) return ProjectType.Node;
        return ProjectType.Other;
    }

    /// <summary>Returns the .NET project to run : a web project when there is one, else the first project found.</summary>
    public static string? FindDotNetProject(string projectPath)
    {
        try
        {
            var projectFiles = Directory.EnumerateFiles(projectPath, "*.csproj", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = DotNetProjectSearchDepth,
                IgnoreInaccessible = true,
            }).ToList();
            return projectFiles.FirstOrDefault(projectFile => ReadTextFile(projectFile).Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase))
                ?? projectFiles.FirstOrDefault();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Returns the Docker Compose file at the root of the project, or null.</summary>
    public static string? FindComposeFile(string projectPath)
        => new[] { "compose.yaml", "compose.yml", "docker-compose.yaml", "docker-compose.yml" }
            .Select(composeFileName => Path.Combine(projectPath, composeFileName))
            .FirstOrDefault(File.Exists);

    /// <summary>Returns the npm scripts of the project (package.json), in their declaration order.</summary>
    public static IReadOnlyList<string> GetNpmScripts(string projectPath)
    {
        var packageJsonContent = ReadTextFile(Path.Combine(projectPath, "package.json"));
        if (packageJsonContent.Length == 0) return Array.Empty<string>();
        try
        {
            using var packageJson = JsonDocument.Parse(packageJsonContent, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            return packageJson.RootElement.TryGetProperty("scripts", out var scriptsElement) && scriptsElement.ValueKind == JsonValueKind.Object
                ? scriptsElement.EnumerateObject().Select(script => script.Name).ToList()
                : Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Returns the content of a text file, or an empty string when it is missing or unreadable.</summary>
    private static string ReadTextFile(string filePath)
    {
        try
        {
            return File.Exists(filePath) ? File.ReadAllText(filePath) : string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}
