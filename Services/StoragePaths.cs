using System.IO;

namespace DevLauncher.Services;

/// <summary>
/// Locations of the persisted data, in %APPDATA%\DevLauncher so that every build and every copy
/// of the executable shares the same settings and profiles.
/// </summary>
public static class StoragePaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DevLauncher");

    public static string SettingsFilePath { get; } = Path.Combine(DataDirectory, "settings.json");

    public static string ProfilesDirectory { get; } = Path.Combine(DataDirectory, "Profiles");

    public static string RecentProjectsFilePath { get; } = Path.Combine(DataDirectory, "recent-projects.json");

    public static string FavoriteProjectsFilePath { get; } = Path.Combine(DataDirectory, "favorite-projects.json");

    public static string LaunchStatisticsFilePath { get; } = Path.Combine(DataDirectory, "launch-statistics.json");

    public static string VirtualHostsFilePath { get; } = Path.Combine(DataDirectory, "virtual-hosts.json");

    /// <summary>Folder of the persistent log, one file per day.</summary>
    public static string LogsDirectory { get; } = Path.Combine(DataDirectory, "Logs");

    /// <summary>Folder of the executable, where the data was stored by the versions prior to the data directory.</summary>
    private static string LegacyDataDirectory => AppDomain.CurrentDomain.BaseDirectory;

    /// <summary>
    /// Creates the data directory and copies the settings and profiles found next to the executable
    /// when they do not exist yet in the data directory. Legacy files are left untouched.
    /// </summary>
    public static void InitializeDataDirectory()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ProfilesDirectory);
        CopyFileIfMissing(Path.Combine(LegacyDataDirectory, "settings.json"), SettingsFilePath);
        var legacyProfilesDirectory = Path.Combine(LegacyDataDirectory, "Profiles");
        if (!Directory.Exists(legacyProfilesDirectory)) return;
        foreach (var legacyProfileFilePath in Directory.EnumerateFiles(legacyProfilesDirectory, "*.json"))
            CopyFileIfMissing(legacyProfileFilePath, Path.Combine(ProfilesDirectory, Path.GetFileName(legacyProfileFilePath)));
    }

    private static void CopyFileIfMissing(string sourceFilePath, string destinationFilePath)
    {
        if (!File.Exists(sourceFilePath) || File.Exists(destinationFilePath)) return;
        try
        {
            File.Copy(sourceFilePath, destinationFilePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A legacy file that cannot be copied is skipped : defaults apply instead.
        }
    }
}
