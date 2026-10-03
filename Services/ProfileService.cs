using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DevLauncher.Models;

namespace DevLauncher.Services;

/// <summary>
/// Saves and reads the launch profiles of each project.
/// Profiles are stored either in the Profiles folder of the data directory, one JSON file per project name,
/// or, when the project shares them, in the .devlauncher.json file at the root of the project, versioned with it.
/// Profiles saved in the legacy format (one boolean per option) are converted and rewritten on read.
/// The last used profile stays personal : it is always stored in the data directory.
/// </summary>
public class ProfileService
{
    /// <summary>File holding the profiles shared with the project.</summary>
    public const string ProjectProfilesFileName = ".devlauncher.json";

    private const int ProjectProfilesFormatVersion = 1;

    private readonly string _profilesDirectory;
    private readonly string _lastUsedProfilesPath;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Content of the .devlauncher.json file of a project.</summary>
    private sealed class ProjectProfilesFile
    {
        public int Version { get; set; } = ProjectProfilesFormatVersion;
        public List<ProjectProfile> Profiles { get; set; } = new();
    }

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

    /// <summary>True when the project holds its own profiles in .devlauncher.json.</summary>
    public static bool IsSharedInProject(string projectPath) => File.Exists(GetProjectProfilesFilePath(projectPath));

    public static string GetProjectProfilesFilePath(string projectPath) => Path.Combine(projectPath, ProjectProfilesFileName);

    /// <summary>
    /// Returns all the profiles of a project, or an empty list when none is saved.
    /// An unreadable local file gives an empty list ; an unreadable shared file throws InvalidDataException,
    /// so that the file of the repository is never overwritten by default profiles.
    /// </summary>
    public List<ProjectProfile> GetProfiles(string projectPath)
        => IsSharedInProject(projectPath) ? ReadProjectProfilesFile(projectPath) : ReadLocalProfiles(GetLocalProfilesFilePath(projectPath));

    /// <summary>Returns a profile by its name.</summary>
    public ProjectProfile? GetProfile(string projectPath, string profileName)
        => GetProfiles(projectPath).FirstOrDefault(profile => profile.Name == profileName);

    /// <summary>Returns the name of the last profile used for a project.</summary>
    public string? GetLastUsedProfile(string projectPath)
        => ReadLastUsedProfiles().GetValueOrDefault(GetProjectKey(projectPath));

    // ════════════════════════════════════════════════════════
    //  WRITE
    // ════════════════════════════════════════════════════════

    /// <summary>Creates or replaces a profile, keeping its position in the list.</summary>
    public void SaveProfile(string projectPath, ProjectProfile profile)
    {
        var profiles = GetProfiles(projectPath);
        var existingIndex = profiles.FindIndex(existingProfile => existingProfile.Name == profile.Name);
        if (existingIndex >= 0) profiles[existingIndex] = profile;
        else profiles.Add(profile);
        WriteProfiles(projectPath, profiles);
    }

    /// <summary>Renames a profile in place and keeps the last used profile consistent.</summary>
    public void RenameProfile(string projectPath, string currentProfileName, string newProfileName)
    {
        var profiles = GetProfiles(projectPath);
        var profileToRename = profiles.FirstOrDefault(profile => profile.Name == currentProfileName);
        if (profileToRename is null) return;
        profileToRename.Name = newProfileName;
        WriteProfiles(projectPath, profiles);
        if (GetLastUsedProfile(projectPath) == currentProfileName) SaveLastUsedProfile(projectPath, newProfileName);
    }

    /// <summary>Deletes a profile by its name.</summary>
    public void DeleteProfile(string projectPath, string profileName)
    {
        var profiles = GetProfiles(projectPath);
        profiles.RemoveAll(profile => profile.Name == profileName);
        WriteProfiles(projectPath, profiles);
    }

    /// <summary>Remembers the last profile used for a project.</summary>
    public void SaveLastUsedProfile(string projectPath, string profileName)
    {
        var projectKey = GetProjectKey(projectPath);
        var lastUsedProfiles = ReadLastUsedProfiles();
        if (lastUsedProfiles.GetValueOrDefault(projectKey) == profileName) return;
        lastUsedProfiles[projectKey] = profileName;
        WriteJsonFile(_lastUsedProfilesPath, lastUsedProfiles);
    }

    /// <summary>Writes the local profiles of the project into its .devlauncher.json file, which then becomes their storage.</summary>
    public void ShareInProject(string projectPath)
    {
        if (IsSharedInProject(projectPath)) return;
        var localProfiles = ReadLocalProfiles(GetLocalProfilesFilePath(projectPath));
        WriteJsonFile(GetProjectProfilesFilePath(projectPath), new ProjectProfilesFile { Profiles = localProfiles });
    }

    /// <summary>Copies the shared profiles into the local storage, then deletes the .devlauncher.json file of the project.</summary>
    public void StopSharingInProject(string projectPath)
    {
        if (!IsSharedInProject(projectPath)) return;
        var sharedProfiles = ReadProjectProfilesFile(projectPath);
        WriteJsonFile(GetLocalProfilesFilePath(projectPath), sharedProfiles);
        File.Delete(GetProjectProfilesFilePath(projectPath));
    }

    // ════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════

    private void WriteProfiles(string projectPath, List<ProjectProfile> profiles)
    {
        if (IsSharedInProject(projectPath)) WriteJsonFile(GetProjectProfilesFilePath(projectPath), new ProjectProfilesFile { Profiles = profiles });
        else WriteJsonFile(GetLocalProfilesFilePath(projectPath), profiles);
    }

    /// <summary>Reads the local profiles file, converting and rewriting the legacy profiles it contains.</summary>
    private static List<ProjectProfile> ReadLocalProfiles(string profilesFilePath)
    {
        if (!File.Exists(profilesFilePath)) return new List<ProjectProfile>();
        try
        {
            if (ParseJson(File.ReadAllText(profilesFilePath)) is not JsonArray profilesArray) return new List<ProjectProfile>();
            var profiles = ConvertProfiles(profilesArray, out var containsLegacyProfiles);
            if (containsLegacyProfiles) TryWriteJsonFile(profilesFilePath, profiles);
            return profiles;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return new List<ProjectProfile>();
        }
    }

    /// <summary>Reads .devlauncher.json : an object with a profiles array, or directly a profiles array.</summary>
    private static List<ProjectProfile> ReadProjectProfilesFile(string projectPath)
    {
        var projectProfilesFilePath = GetProjectProfilesFilePath(projectPath);
        try
        {
            var rootNode = ParseJson(File.ReadAllText(projectProfilesFilePath));
            var profilesArray = rootNode switch
            {
                JsonArray rootArray => rootArray,
                JsonObject rootObject => rootObject["profiles"] as JsonArray ?? new JsonArray(),
                _ => throw new JsonException("racine JSON inattendue"),
            };
            return ConvertProfiles(profilesArray, out _);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"{ProjectProfilesFileName} illisible ({exception.Message})", exception);
        }
    }

    private static JsonNode? ParseJson(string jsonText)
        => JsonNode.Parse(jsonText, new JsonNodeOptions { PropertyNameCaseInsensitive = true }, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });

    private static List<ProjectProfile> ConvertProfiles(JsonArray profilesArray, out bool containsLegacyProfiles)
    {
        var profiles = new List<ProjectProfile>();
        containsLegacyProfiles = false;
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
        return profiles;
    }

    private Dictionary<string, string> ReadLastUsedProfiles()
        => ReadJsonFile<Dictionary<string, string>>(_lastUsedProfilesPath) ?? new Dictionary<string, string>();

    private static ProjectProfile? DeserializeProfile(JsonObject profileNode)
    {
        try
        {
            var profile = profileNode.Deserialize<ProjectProfile>(_jsonOptions);
            if (profile is null || string.IsNullOrWhiteSpace(profile.Name)) return null;
            profile.Tools ??= new Dictionary<string, ToolSelection>();
            return profile;
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

    /// <summary>Local profiles and last used profiles are keyed by the folder name of the project.</summary>
    private static string GetProjectKey(string projectPath) => Path.GetFileName(Path.TrimEndingDirectorySeparator(projectPath));

    private string GetLocalProfilesFilePath(string projectPath)
        => Path.Combine(_profilesDirectory, $"{SanitizeFileName(GetProjectKey(projectPath))}.json");

    /// <summary>Replaces the characters forbidden in a file name.</summary>
    private static string SanitizeFileName(string name)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(character => invalidCharacters.Contains(character) ? '_' : character));
    }
}
