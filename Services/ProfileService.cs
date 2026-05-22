using System.IO;
using System.Text.Json;
using DevLauncher.Models;

namespace DevLauncher.Services;

/// <summary>
/// Gère la sauvegarde et la lecture des profils par projet.
/// Les profils sont stockés dans : C:\Users\sam\DevLauncher\Profiles\
/// </summary>
public class ProfileService
{
    private readonly string _profilesDir;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ProfileService()
    {
        // Dossier Profiles/ à côté du .exe
        _profilesDir = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Profiles");

        Directory.CreateDirectory(_profilesDir);
    }

    // ════════════════════════════════════════════════════════
    //  LECTURE
    // ════════════════════════════════════════════════════════

    /// <summary>Retourne tous les profils d'un projet.</summary>
    public List<ProjectProfile> GetProfiles(string projectName)
    {
        var path = GetFilePath(projectName);
        if (!File.Exists(path)) return new List<ProjectProfile>();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<ProjectProfile>>(json, _jsonOptions)
                   ?? new List<ProjectProfile>();
        }
        catch
        {
            return new List<ProjectProfile>();
        }
    }

    /// <summary>Retourne un profil spécifique par son nom.</summary>
    public ProjectProfile? GetProfile(string projectName, string profileName)
    {
        var profiles = GetProfiles(projectName);
        return profiles.FirstOrDefault(p => p.Name == profileName);
    }

    // ════════════════════════════════════════════════════════
    //  ÉCRITURE
    // ════════════════════════════════════════════════════════

    /// <summary>Sauvegarde ou met à jour un profil.</summary>
    public void SaveProfile(string projectName, ProjectProfile profile)
    {
        var profiles = GetProfiles(projectName);
        var existing = profiles.FindIndex(p => p.Name == profile.Name);

        if (existing >= 0)
            profiles[existing] = profile; // Mise à jour
        else
            profiles.Add(profile);        // Nouveau

        WriteProfiles(projectName, profiles);
    }

    /// <summary>Supprime un profil par son nom.</summary>
    public void DeleteProfile(string projectName, string profileName)
    {
        var profiles = GetProfiles(projectName);
        profiles.RemoveAll(p => p.Name == profileName);
        WriteProfiles(projectName, profiles);
    }

    // ════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════

    private void WriteProfiles(string projectName, List<ProjectProfile> profiles)
    {
        var json = JsonSerializer.Serialize(profiles, _jsonOptions);
        File.WriteAllText(GetFilePath(projectName), json);
    }

    private string GetFilePath(string projectName)
        => Path.Combine(_profilesDir, $"{SanitizeFileName(projectName)}.json");

    /// <summary>Supprime les caractères interdits dans un nom de fichier.</summary>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }
}