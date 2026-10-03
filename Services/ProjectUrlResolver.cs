using System.IO;
using DevLauncher.Models;

namespace DevLauncher.Services;

/// <summary>Finds the local URL of a project without waiting : configured ports, Apache folder, or URL announced by a running server.</summary>
public static class ProjectUrlResolver
{
    /// <summary>
    /// Returns the URL of the project, or null when it is only known once its development server has announced it.
    /// A project served by Apache uses its virtual host when it has one.
    /// </summary>
    public static string? GetKnownUrl(string projectPath, ProjectType projectType, string? announcedUrl, string? virtualHostName = null) => projectType switch
    {
        ProjectType.Symfony => announcedUrl ?? $"http://127.0.0.1:{AppSettings.SymfonyPort}",
        ProjectType.Other or ProjectType.WordPress => GetApacheUrl(projectPath, virtualHostName),
        _ => announcedUrl,
    };

    /// <summary>Address of a project served by Apache : its virtual host, or else its folder under the Apache document root.</summary>
    public static string? GetApacheUrl(string projectPath, string? virtualHostName)
        => virtualHostName is not null
            ? BuildVirtualHostUrl(virtualHostName, AppSettings.LocalWebPort)
            : BuildApacheUrl(projectPath, AppSettings.ApacheDocumentRoot, AppSettings.LocalWebPort);

    public static string BuildVirtualHostUrl(string virtualHostName, int apachePort) => $"http://{virtualHostName}{(apachePort == 80 ? string.Empty : $":{apachePort}")}/";

    /// <summary>
    /// Returns the URL of a project served by Apache : its path relative to the Apache document root.
    /// Returns null for a project located outside of it.
    /// </summary>
    public static string? BuildApacheUrl(string projectPath, string apacheDocumentRoot, int apachePort)
    {
        var relativeProjectPath = Path.GetRelativePath(Path.GetFullPath(apacheDocumentRoot), Path.GetFullPath(projectPath));
        if (relativeProjectPath.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativeProjectPath)) return null;
        var urlPath = string.Join('/', relativeProjectPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Select(Uri.EscapeDataString));
        return $"http://localhost{(apachePort == 80 ? string.Empty : $":{apachePort}")}/{urlPath}/";
    }
}
