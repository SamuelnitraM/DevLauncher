using System.IO;
using System.Text.Json;

namespace DevLauncher.Services;

/// <summary>
/// Remembers the projects pinned as favorites, in the order they were pinned.
/// </summary>
public class FavoriteProjectsService
{
    private readonly string _favoritesFilePath;

    public FavoriteProjectsService() : this(StoragePaths.FavoriteProjectsFilePath)
    {
    }

    public FavoriteProjectsService(string favoritesFilePath)
    {
        _favoritesFilePath = favoritesFilePath;
    }

    /// <summary>Returns the full paths of the favorite projects.</summary>
    public List<string> GetFavoriteProjectPaths()
    {
        if (!File.Exists(_favoritesFilePath)) return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_favoritesFilePath))?
                .Where(projectPath => !string.IsNullOrWhiteSpace(projectPath))
                .ToList() ?? new List<string>();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return new List<string>();
        }
    }

    public bool IsFavorite(string projectPath) => GetFavoriteProjectPaths().Any(favoritePath => PathComparer.AreSame(favoritePath, projectPath));

    /// <summary>Pins the project when it is not a favorite, unpins it otherwise. Returns true when it is now a favorite.</summary>
    public bool ToggleFavorite(string projectPath)
    {
        var favoriteProjectPaths = GetFavoriteProjectPaths();
        var removedCount = favoriteProjectPaths.RemoveAll(favoritePath => PathComparer.AreSame(favoritePath, projectPath));
        var isNowFavorite = removedCount == 0;
        if (isNowFavorite) favoriteProjectPaths.Add(projectPath);
        Directory.CreateDirectory(Path.GetDirectoryName(_favoritesFilePath)!);
        File.WriteAllText(_favoritesFilePath, JsonSerializer.Serialize(favoriteProjectPaths, new JsonSerializerOptions { WriteIndented = true }));
        return isNowFavorite;
    }
}
