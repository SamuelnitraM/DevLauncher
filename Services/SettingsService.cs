using System.IO;
using System.Text.Json;

namespace DevLauncher.Services;

/// <summary>
/// Sauvegarde et charge les paramètres de l'application dans un fichier JSON.
/// Stocké dans : C:\Users\sam\DevLauncher\bin\...\settings.json
/// </summary>
public static class SettingsService
{
    private static readonly string _settingsPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "settings.json");

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ════════════════════════════════════════════════════════
    //  SAUVEGARDE
    // ════════════════════════════════════════════════════════

    public static void Save()
    {
        var data = new SettingsData
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
        };

        var json = JsonSerializer.Serialize(data, _jsonOptions);
        File.WriteAllText(_settingsPath, json);
    }

    // ════════════════════════════════════════════════════════
    //  CHARGEMENT
    // ════════════════════════════════════════════════════════

    public static void Load()
    {
        if (!File.Exists(_settingsPath)) return;

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var data = JsonSerializer.Deserialize<SettingsData>(json, _jsonOptions);
            if (data is null) return;

            AppSettings.HtdocsPath = data.HtdocsPath;
            AppSettings.XamppDir = data.XamppDir;
            AppSettings.ApacheExe = data.ApacheExe;
            AppSettings.MySQLExe = data.MySQLExe;
            AppSettings.MySQLConfig = data.MySQLConfig;
            AppSettings.FileZillaExe = data.FileZillaExe;
            AppSettings.XamppPanel = data.XamppPanel;
            AppSettings.MercureDir = data.MercureDir;
            AppSettings.VSCodeExecutable = data.VSCodeExecutable;
            AppSettings.VisualStudioExecutable = data.VisualStudioExecutable;
            AppSettings.ChromeExe = data.ChromeExe;
            AppSettings.FirefoxExe = data.FirefoxExe;
            AppSettings.SymfonyPort = data.SymfonyPort;
        }
        catch { /* Paramètres par défaut si le fichier est corrompu */ }
    }

    // ════════════════════════════════════════════════════════
    //  MODÈLE DE DONNÉES
    // ════════════════════════════════════════════════════════

    private class SettingsData
    {
        public string HtdocsPath { get; set; } = AppSettings.HtdocsPath;
        public string XamppDir { get; set; } = AppSettings.XamppDir;
        public string ApacheExe { get; set; } = AppSettings.ApacheExe;
        public string MySQLExe { get; set; } = AppSettings.MySQLExe;
        public string MySQLConfig { get; set; } = AppSettings.MySQLConfig;
        public string FileZillaExe { get; set; } = AppSettings.FileZillaExe;
        public string XamppPanel { get; set; } = AppSettings.XamppPanel;
        public string MercureDir { get; set; } = AppSettings.MercureDir;
        public string VSCodeExecutable { get; set; } = AppSettings.VSCodeExecutable;
        public string VisualStudioExecutable { get; set; } = AppSettings.VisualStudioExecutable;
        public string ChromeExe { get; set; } = AppSettings.ChromeExe;
        public string FirefoxExe { get; set; } = AppSettings.FirefoxExe;
        public int SymfonyPort { get; set; } = AppSettings.SymfonyPort;
    }
}