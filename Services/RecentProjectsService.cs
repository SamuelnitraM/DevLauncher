using System.IO;
using System.Text.Json;

namespace DevLauncher.Services;

/// <summary>
/// Remembers the most recently launched projects, most recent first.
/// </summary>
public class RecentProjectsService
{
    public const int MaximumRecentProjectCount = 5;

    /// <summary>Returns the full paths of the recently launched projects, most recent first.</summary>
    public List<string> GetRecentProjectPaths()
    {
        if (!File.Exists(StoragePaths.RecentProjectsFilePath)) return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(StoragePaths.RecentProjectsFilePath)) ?? new List<string>();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return new List<string>();
        }
    }

    /// <summary>Moves a launched project to the top of the recent projects.</summary>
    public void RegisterLaunch(string projectPath)
    {
        var recentProjectPaths = GetRecentProjectPaths();
        recentProjectPaths.RemoveAll(recentProjectPath => recentProjectPath.Equals(projectPath, StringComparison.OrdinalIgnoreCase));
        recentProjectPaths.Insert(0, projectPath);
        var trimmedRecentProjectPaths = recentProjectPaths.Take(MaximumRecentProjectCount).ToList();
        try
        {
            File.WriteAllText(StoragePaths.RecentProjectsFilePath, JsonSerializer.Serialize(trimmedRecentProjectPaths, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Recent projects are a convenience : a failed write keeps the previous list.
        }
    }
}
