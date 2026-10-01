using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DevLauncher.Models;

namespace DevLauncher.Services;

/// <summary>
/// Saves and reads the launch profiles of each project.
/// Profiles are stored in the Profiles folder of the data directory, one JSON file per project.
/// Profiles saved in the legacy format (one boolean per option) are converted and rewritten on read.
/// </summary>
public class ProfileService
{
    private readonly string _profilesDirectory;
    private readonly string _lastUsedProfilesPath;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public ProfileService() : this(StoragePaths.ProfilesDirectory)
    {
    }

    public ProfileService(string profilesDirectory)
    {
        _profilesDirectory = profilesDirectory;
        _lastUsedProfilesPath = Path.Combine(_profilesDirectory, "last-used.json");
        Directory.CreateDirectory(_profilesDirectory);
    }

    // ════════════════════════════════════════════════════════
    //  READ
    // ════════════════════════════════════════════════════════

    /// <summary>Returns all the profiles of a project, or an empty list when none is saved or the file is unreadable.</summary>
    public List<ProjectProfile> GetProfiles(string projectName)
    {
        var profilesFilePath = GetProfilesFilePath(projectName);
        var profilesArray = ReadJsonArray(profilesFilePath);
        if (profilesArray is null) return new List<ProjectProfile>();
        var profiles = new List<ProjectProfile>();
        var containsLegacyProfiles = false;
        foreach (var profileNode in profilesArray.OfType<JsonObject>())
        {
            if (LegacyProfileConverter.IsLegacyProfile(profileNode))
            {
                profiles.Add(LegacyProfileConverter.Convert(profileNode));
                containsLegacyProfiles = true;
            }
            else if (DeserializeProfile(profileNode) is { } profile)
            {
                profiles.Add(profile);
            }
        }
        if (containsLegacyProfiles) TryWriteJsonFile(profilesFilePath, profiles);
        return profiles;
    }

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

    private static JsonArray? ReadJsonArray(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            return JsonNode.Parse(File.ReadAllText(filePath), new JsonNodeOptions { PropertyNameCaseInsensitive = true }) as JsonArray;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static ProjectProfile? DeserializeProfile(JsonObject profileNode)
    {
        try
        {
            return profileNode.Deserialize<ProjectProfile>(_jsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void TryWriteJsonFile<T>(string filePath, T content)
    {
        try
        {
            WriteJsonFile(filePath, content);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The converted profiles are still returned : the file is rewritten on the next save.
        }
    }

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
