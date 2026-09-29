using System.IO;
using System.Text.Json;
using DevLauncher.Models;

namespace DevLauncher.Services;

/// <summary>
/// Saves and reads the launch profiles of each project.
/// Profiles are stored in the Profiles folder next to the executable, one JSON file per project.
/// </summary>
public class ProfileService
{
    private readonly string _profilesDirectory;
    private readonly string _lastUsedProfilesPath;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ProfileService()
    {
        _profilesDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Profiles");
        _lastUsedProfilesPath = Path.Combine(_profilesDirectory, "last-used.json");
        Directory.CreateDirectory(_profilesDirectory);
    }

    // ════════════════════════════════════════════════════════
    //  READ
    // ════════════════════════════════════════════════════════

    /// <summary>Returns all the profiles of a project, or an empty list when none is saved or the file is unreadable.</summary>
    public List<ProjectProfile> GetProfiles(string projectName)
        => ReadJsonFile<List<ProjectProfile>>(GetProfilesFilePath(projectName)) ?? new List<ProjectProfile>();

    /// <summary>Returns a profile by its name.</summary>
    public ProjectProfile? GetProfile(string projectName, string profileName)
        => GetProfiles(projectName).FirstOrDefault(profile => profile.Name == profileName);

    /// <summary>Returns the name of the last profile used for a project.</summary>
    public string? GetLastUsedProfile(string projectName)
        => ReadLastUsedProfiles().GetValueOrDefault(projectName);

    // ════════════════════════════════════════════════════════
    //  WRITE
    // ════════════════════════════════════════════════════════

    /// <summary>Creates or replaces a profile, keeping its position in the list.</summary>
    public void SaveProfile(string projectName, ProjectProfile profile)
    {
        var profiles = GetProfiles(projectName);
        var existingIndex = profiles.FindIndex(existingProfile => existingProfile.Name == profile.Name);
        if (existingIndex >= 0) profiles[existingIndex] = profile;
        else profiles.Add(profile);
        WriteJsonFile(GetProfilesFilePath(projectName), profiles);
    }

    /// <summary>Renames a profile in place and keeps the last used profile consistent.</summary>
    public void RenameProfile(string projectName, string currentProfileName, string newProfileName)
    {
        var profiles = GetProfiles(projectName);
        var profileToRename = profiles.FirstOrDefault(profile => profile.Name == currentProfileName);
        if (profileToRename is null) return;
        profileToRename.Name = newProfileName;
        WriteJsonFile(GetProfilesFilePath(projectName), profiles);
        if (GetLastUsedProfile(projectName) == currentProfileName) SaveLastUsedProfile(projectName, newProfileName);
    }

    /// <summary>Deletes a profile by its name.</summary>
    public void DeleteProfile(string projectName, string profileName)
    {
        var profiles = GetProfiles(projectName);
        profiles.RemoveAll(profile => profile.Name == profileName);
        WriteJsonFile(GetProfilesFilePath(projectName), profiles);
    }

    /// <summary>Remembers the last profile used for a project.</summary>
    public void SaveLastUsedProfile(string projectName, string profileName)
    {
        var lastUsedProfiles = ReadLastUsedProfiles();
        if (lastUsedProfiles.GetValueOrDefault(projectName) == profileName) return;
        lastUsedProfiles[projectName] = profileName;
        WriteJsonFile(_lastUsedProfilesPath, lastUsedProfiles);
    }

    // ════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════

    private Dictionary<string, string> ReadLastUsedProfiles()
        => ReadJsonFile<Dictionary<string, string>>(_lastUsedProfilesPath) ?? new Dictionary<string, string>();

    private static T? ReadJsonFile<T>(string filePath) where T : class
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(filePath), _jsonOptions);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteJsonFile<T>(string filePath, T content)
        => File.WriteAllText(filePath, JsonSerializer.Serialize(content, _jsonOptions));

    private string GetProfilesFilePath(string projectName)
        => Path.Combine(_profilesDirectory, $"{SanitizeFileName(projectName)}.json");

    /// <summary>Replaces the characters forbidden in a file name.</summary>
    private static string SanitizeFileName(string name)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(character => invalidCharacters.Contains(character) ? '_' : character));
    }
}
