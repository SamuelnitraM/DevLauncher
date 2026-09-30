using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using DevLauncher.Models;
using DevLauncher.Services.Assistants;

namespace DevLauncher.Services;

/// <summary>
/// Saves and loads the application settings in settings.json, in the data directory.
/// </summary>
public static class SettingsService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // ════════════════════════════════════════════════════════
    //  SAVE
    // ════════════════════════════════════════════════════════

    public static void Save()
    {
        var settingsData = new SettingsData
        {
            HtdocsPath = AppSettings.HtdocsPath,
            XamppDir = AppSettings.XamppDir,
            ApacheExe = AppSettings.ApacheExe,
            MySQLExe = AppSettings.MySQLExe,
            MySQLConfig = AppSettings.MySQLConfig,
            FileZillaExe = AppSettings.FileZillaExe,
            XamppPanel = AppSettings.XamppPanel,
            MercureDir = AppSettings.MercureDir,
            VSCodeExecutable = AppSettings.VSCodeExecutable,
            VisualStudioExecutable = AppSettings.VisualStudioExecutable,
            ChromeExe = AppSettings.ChromeExe,
            FirefoxExe = AppSettings.FirefoxExe,
            SymfonyPort = AppSettings.SymfonyPort,
            LocalWebPort = AppSettings.LocalWebPort,
            HostServicesInVSCode = AppSettings.HostServicesInVSCode,
            Assistants = AppSettings.Assistants,
        };
        File.WriteAllText(StoragePaths.SettingsFilePath, JsonSerializer.Serialize(settingsData, _jsonOptions));
    }

    // ════════════════════════════════════════════════════════
    //  LOAD
    // ════════════════════════════════════════════════════════

    /// <summary>Applies settings.json to AppSettings. Missing, empty or invalid values keep their defaults.</summary>
    public static void Load()
    {
        if (!File.Exists(StoragePaths.SettingsFilePath)) return;
        SettingsData? settingsData;
        try
        {
            settingsData = JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(StoragePaths.SettingsFilePath), _jsonOptions);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return;
        }
        if (settingsData is null) return;
        AppSettings.HtdocsPath = ValueOrDefault(settingsData.HtdocsPath, AppSettings.HtdocsPath);
        AppSettings.XamppDir = ValueOrDefault(settingsData.XamppDir, AppSettings.XamppDir);
        AppSettings.ApacheExe = ValueOrDefault(settingsData.ApacheExe, AppSettings.ApacheExe);
        AppSettings.MySQLExe = ValueOrDefault(settingsData.MySQLExe, AppSettings.MySQLExe);
        AppSettings.MySQLConfig = ValueOrDefault(settingsData.MySQLConfig, AppSettings.MySQLConfig);
        AppSettings.FileZillaExe = ValueOrDefault(settingsData.FileZillaExe, AppSettings.FileZillaExe);
        AppSettings.XamppPanel = ValueOrDefault(settingsData.XamppPanel, AppSettings.XamppPanel);
        AppSettings.MercureDir = ValueOrDefault(settingsData.MercureDir, AppSettings.MercureDir);
        AppSettings.VSCodeExecutable = ValueOrDefault(settingsData.VSCodeExecutable, AppSettings.VSCodeExecutable);
        AppSettings.VisualStudioExecutable = ValueOrDefault(settingsData.VisualStudioExecutable, AppSettings.VisualStudioExecutable);
        AppSettings.ChromeExe = ValueOrDefault(settingsData.ChromeExe, AppSettings.ChromeExe);
        AppSettings.FirefoxExe = ValueOrDefault(settingsData.FirefoxExe, AppSettings.FirefoxExe);
        AppSettings.SymfonyPort = IsValidPort(settingsData.SymfonyPort) ? settingsData.SymfonyPort : AppSettings.SymfonyPort;
        AppSettings.LocalWebPort = IsValidPort(settingsData.LocalWebPort) ? settingsData.LocalWebPort : AppSettings.LocalWebPort;
        AppSettings.HostServicesInVSCode = settingsData.HostServicesInVSCode;
        AppSettings.Assistants = AssistantCatalog.MergeWithDefaults(settingsData.Assistants);
    }

    /// <summary>Returns true when the value is a usable TCP port.</summary>
    public static bool IsValidPort(int port) => port is >= 1 and <= 65535;

    private static string ValueOrDefault(string? value, string defaultValue)
        => string.IsNullOrWhiteSpace(value) ? defaultValue : value;

    // ════════════════════════════════════════════════════════
    //  DATA MODEL
    // ════════════════════════════════════════════════════════

    private class SettingsData
    {
        public string? HtdocsPath { get; set; }
        public string? XamppDir { get; set; }
        public string? ApacheExe { get; set; }
        public string? MySQLExe { get; set; }
        public string? MySQLConfig { get; set; }
        public string? FileZillaExe { get; set; }
        public string? XamppPanel { get; set; }
        public string? MercureDir { get; set; }
        public string? VSCodeExecutable { get; set; }
        public string? VisualStudioExecutable { get; set; }
        public string? ChromeExe { get; set; }
        public string? FirefoxExe { get; set; }
        public int SymfonyPort { get; set; }
        public int LocalWebPort { get; set; }
        public bool HostServicesInVSCode { get; set; }
        public List<AssistantSettings>? Assistants { get; set; }
    }
}
