using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DevLauncher.Services;
using DevLauncher.Services.Assistants;
using Microsoft.Win32;

namespace DevLauncher.Views;

public partial class SettingsWindow : Window
{
    private readonly List<AssistantSettingsRow> _assistantSettingsRows = AssistantCatalog.Definitions
        .Select(assistantDefinition => new AssistantSettingsRow(assistantDefinition, AssistantCatalog.GetSettings(assistantDefinition.Id)))
        .ToList();

    private readonly Dictionary<DetectableSetting, TextBox> _pathBoxesBySetting;
    private readonly Brush _validBorderBrush;

    public SettingsWindow()
    {
        InitializeComponent();
        _pathBoxesBySetting = new Dictionary<DetectableSetting, TextBox>
        {
            [DetectableSetting.XamppDirectory] = XamppDirBox,
            [DetectableSetting.ApacheExecutable] = ApacheExeBox,
            [DetectableSetting.MySqlExecutable] = MySQLExeBox,
            [DetectableSetting.MySqlConfiguration] = MySQLConfigBox,
            [DetectableSetting.FileZillaExecutable] = FileZillaExeBox,
            [DetectableSetting.XamppPanel] = XamppPanelBox,
            [DetectableSetting.VSCodeExecutable] = VSCodeBox,
            [DetectableSetting.VisualStudioExecutable] = VisualStudioBox,
            [DetectableSetting.ChromeExecutable] = ChromeBox,
            [DetectableSetting.FirefoxExecutable] = FirefoxBox,
        };
        _validBorderBrush = XamppDirBox.BorderBrush;
        LoadSettings();
        foreach (var pathBox in _pathBoxesBySetting.Values.Append(ProjectRootsBox).Append(ExtraProjectsBox).Append(MercureDirBox))
            pathBox.TextChanged += (_, _) => ValidatePaths();
        ValidatePaths();
    }

    /// <summary>True when the settings and profiles were replaced by an import.</summary>
    public bool IsImported { get; private set; }

    // ════════════════════════════════════════════════════════
    //  LOAD
    // ════════════════════════════════════════════════════════

    private void LoadSettings()
    {
        ProjectRootsBox.Text = string.Join(Environment.NewLine, AppSettings.ProjectRoots);
        ExtraProjectsBox.Text = string.Join(Environment.NewLine, AppSettings.ExtraProjectPaths);
        ExcludedFoldersBox.Text = string.Join(", ", AppSettings.ExcludedFolderNames);
        XamppDirBox.Text = AppSettings.XamppDir;
        ApacheExeBox.Text = AppSettings.ApacheExe;
        MySQLExeBox.Text = AppSettings.MySQLExe;
        MySQLConfigBox.Text = AppSettings.MySQLConfig;
        FileZillaExeBox.Text = AppSettings.FileZillaExe;
        XamppPanelBox.Text = AppSettings.XamppPanel;
        MercureDirBox.Text = AppSettings.MercureDir;
        VSCodeBox.Text = AppSettings.VSCodeExecutable;
        VisualStudioBox.Text = AppSettings.VisualStudioExecutable;
        ChromeBox.Text = AppSettings.ChromeExe;
        FirefoxBox.Text = AppSettings.FirefoxExe;
        SymfonyPortBox.Text = AppSettings.SymfonyPort.ToString();
        LocalWebPortBox.Text = AppSettings.LocalWebPort.ToString();
        HostServicesInVSCodeBox.IsChecked = AppSettings.HostServicesInVSCode;
        DetailedLoggingBox.IsChecked = AppSettings.DetailedLogging;
        AssistantsItemsControl.ItemsSource = _assistantSettingsRows;
    }

    // ════════════════════════════════════════════════════════
    //  SAVE
    // ════════════════════════════════════════════════════════

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadPort(SymfonyPortBox, "Symfony", out var symfonyPort) || !TryReadPort(LocalWebPortBox, "Apache", out var localWebPort)) return;
        AppSettings.ProjectRoots = SplitEntries(ProjectRootsBox.Text, '\n');
        AppSettings.ExtraProjectPaths = SplitEntries(ExtraProjectsBox.Text, '\n');
        AppSettings.ExcludedFolderNames = SplitEntries(ExcludedFoldersBox.Text, ',');
        AppSettings.XamppDir = XamppDirBox.Text.Trim();
        AppSettings.ApacheExe = ApacheExeBox.Text.Trim();
        AppSettings.MySQLExe = MySQLExeBox.Text.Trim();
        AppSettings.MySQLConfig = MySQLConfigBox.Text.Trim();
        AppSettings.FileZillaExe = FileZillaExeBox.Text.Trim();
        AppSettings.XamppPanel = XamppPanelBox.Text.Trim();
        AppSettings.MercureDir = MercureDirBox.Text.Trim();
        AppSettings.VSCodeExecutable = VSCodeBox.Text.Trim();
        AppSettings.VisualStudioExecutable = VisualStudioBox.Text.Trim();
        AppSettings.ChromeExe = ChromeBox.Text.Trim();
        AppSettings.FirefoxExe = FirefoxBox.Text.Trim();
        AppSettings.SymfonyPort = symfonyPort;
        AppSettings.LocalWebPort = localWebPort;
        AppSettings.HostServicesInVSCode = HostServicesInVSCodeBox.IsChecked == true;
        AppSettings.DetailedLogging = DetailedLoggingBox.IsChecked == true;
        AppSettings.Assistants = _assistantSettingsRows.Select(assistantSettingsRow => assistantSettingsRow.ToSettings()).ToList();
        try
        {
            SettingsService.Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowValidationError($"❌ Sauvegarde impossible : {exception.Message}");
            return;
        }
        DialogResult = true;
    }

    private static List<string> SplitEntries(string text, char separator)
        => text.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private bool TryReadPort(TextBox portBox, string serverName, out int port)
    {
        if (int.TryParse(portBox.Text, out port) && SettingsService.IsValidPort(port)) return true;
        ShowValidationError($"❌ Port {serverName} invalide (1-65535)");
        return false;
    }

    private void ShowValidationError(string message)
    {
        StatusText.Foreground = (Brush)FindResource("AccentRedBrush");
        StatusText.Text = message;
    }

    // ════════════════════════════════════════════════════════
    //  VALIDATION AND DETECTION
    // ════════════════════════════════════════════════════════

    /// <summary>Outlines in red every path that does not exist, and sums them up in the header.</summary>
    private void ValidatePaths()
    {
        var invalidPathCount = 0;
        foreach (var (setting, pathBox) in _pathBoxesBySetting)
            invalidPathCount += MarkPathBox(pathBox, SettingsAutoDetector.IsValid(SettingsAutoDetector.GetPathKind(setting), pathBox.Text));
        invalidPathCount += MarkPathBox(MercureDirBox, string.IsNullOrWhiteSpace(MercureDirBox.Text) || SettingsAutoDetector.IsValid(SettingPathKind.Folder, MercureDirBox.Text));
        foreach (var foldersBox in new[] { ProjectRootsBox, ExtraProjectsBox })
        {
            var folderPaths = SplitEntries(foldersBox.Text, '\n');
            invalidPathCount += MarkPathBox(foldersBox, folderPaths.All(folderPath => SettingsAutoDetector.IsValid(SettingPathKind.Folder, folderPath)));
        }
        ValidationSummaryText.Text = invalidPathCount == 0
            ? string.Empty
            : $"🔴 {invalidPathCount} chemin(s) introuvable(s), encadré(s) en rouge — « Détecter automatiquement » peut les corriger";
    }

    /// <summary>Returns 1 when the path is invalid, 0 otherwise.</summary>
    private int MarkPathBox(TextBox pathBox, bool isValid)
    {
        pathBox.BorderBrush = isValid ? _validBorderBrush : (Brush)FindResource("AccentRedBrush");
        pathBox.ToolTip = isValid ? null : "🔴 Introuvable sur ce poste";
        return isValid ? 0 : 1;
    }

    /// <summary>Fills the invalid paths with the installations found on this computer. Valid paths are kept as typed.</summary>
    private async void AutoDetect_Click(object sender, RoutedEventArgs e)
    {
        AutoDetectButton.IsEnabled = false;
        StatusText.Foreground = (Brush)FindResource("TextSecondaryBrush");
        StatusText.Text = "🔎 Recherche des installations…";
        try
        {
            var detectedPaths = await Task.Run(SettingsAutoDetector.DetectAll);
            var filledSettingNames = new List<string>();
            foreach (var (setting, detectedPath) in detectedPaths)
            {
                var pathBox = _pathBoxesBySetting[setting];
                if (SettingsAutoDetector.IsValid(SettingsAutoDetector.GetPathKind(setting), pathBox.Text) || pathBox.Text.Trim() == detectedPath) continue;
                pathBox.Text = detectedPath;
                filledSettingNames.Add(setting.ToString());
            }
            StatusText.Text = filledSettingNames.Count == 0
                ? $"✅ {detectedPaths.Count} installation(s) trouvée(s), aucun chemin à corriger"
                : $"✅ {filledSettingNames.Count} chemin(s) renseigné(s) automatiquement — vérifie puis sauvegarde";
        }
        finally
        {
            AutoDetectButton.IsEnabled = true;
        }
    }

    // ════════════════════════════════════════════════════════
    //  LOG AND DATA TRANSFER
    // ════════════════════════════════════════════════════════

    private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(StoragePaths.LogsDirectory);
        Process.Start(new ProcessStartInfo(StoragePaths.LogsDirectory) { UseShellExecute = true })?.Dispose();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var exportDialog = new SaveFileDialog
        {
            Title = "Exporter les paramètres et profils",
            Filter = "Export DevLauncher (*.json)|*.json",
            FileName = $"DevLauncher-export-{DateTime.Now:yyyy-MM-dd}.json",
        };
        if (exportDialog.ShowDialog(this) != true) return;
        try
        {
            var exportedFileCount = new DataPortabilityService().Export(exportDialog.FileName);
            StatusText.Foreground = (Brush)FindResource("AccentGreenBrush");
            StatusText.Text = $"✅ {exportedFileCount} fichier(s) exporté(s)";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowValidationError($"❌ Export impossible : {exception.Message}");
        }
    }

    /// <summary>Imports an export file, reloads the settings and closes the window so that the main window reloads everything.</summary>
    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var importDialog = new OpenFileDialog
        {
            Title = "Importer des paramètres et profils",
            Filter = "Export DevLauncher (*.json)|*.json|Tous les fichiers (*.*)|*.*",
        };
        if (importDialog.ShowDialog(this) != true) return;
        var confirmation = MessageBox.Show(this,
            "Importer ce fichier ?\n\nLes paramètres et les profils du même nom sont remplacés, les modifications non sauvegardées de cette fenêtre sont perdues.",
            "Importer", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes) return;
        try
        {
            new DataPortabilityService().Import(importDialog.FileName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ShowValidationError($"❌ Import impossible : {exception.Message}");
            return;
        }
        SettingsService.Load();
        IsImported = true;
        DialogResult = true;
    }

    // ════════════════════════════════════════════════════════
    //  BROWSE
    // ════════════════════════════════════════════════════════

    /// <summary>Opens a folder or file picker (button Tag "Folder" or "File") and fills the TextBox of the same row.</summary>
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button browseButton || browseButton.Parent is not Grid settingRow) return;
        var pathBox = settingRow.Children.OfType<TextBox>().FirstOrDefault();
        if (pathBox is null) return;
        var currentPath = pathBox.Text.Trim();
        if (browseButton.Tag as string == "Folder")
        {
            var folderDialog = new OpenFolderDialog { Title = "Sélectionne un dossier" };
            if (Directory.Exists(currentPath)) folderDialog.InitialDirectory = currentPath;
            if (folderDialog.ShowDialog(this) == true) pathBox.Text = folderDialog.FolderName;
            return;
        }
        var fileDialog = new OpenFileDialog
        {
            Title = "Sélectionne un fichier",
            Filter = "Exécutables (*.exe)|*.exe|Tous les fichiers (*.*)|*.*",
        };
        var currentDirectory = GetExistingDirectory(currentPath);
        if (currentDirectory is not null) fileDialog.InitialDirectory = currentDirectory;
        if (fileDialog.ShowDialog(this) == true) pathBox.Text = fileDialog.FileName;
    }

    private void AddProjectRoot_Click(object sender, RoutedEventArgs e) => AppendPickedFolder(ProjectRootsBox, "Sélectionne un dossier contenant des projets");

    private void AddExtraProject_Click(object sender, RoutedEventArgs e) => AppendPickedFolder(ExtraProjectsBox, "Sélectionne le dossier du projet");

    /// <summary>Adds a picked folder as a new line of a multi-line path box.</summary>
    private void AppendPickedFolder(TextBox pathsBox, string dialogTitle)
    {
        var folderDialog = new OpenFolderDialog { Title = dialogTitle };
        if (folderDialog.ShowDialog(this) != true) return;
        pathsBox.Text = string.IsNullOrWhiteSpace(pathsBox.Text)
            ? folderDialog.FolderName
            : pathsBox.Text.TrimEnd() + Environment.NewLine + folderDialog.FolderName;
    }

    /// <summary>Picks the executable or the shortcut of an assistant desktop application.</summary>
    private void BrowseAssistantApplication_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AssistantSettingsRow assistantSettingsRow }) return;
        var applicationDialog = new OpenFileDialog
        {
            Title = "Sélectionne l'application",
            Filter = "Applications et raccourcis (*.exe;*.lnk)|*.exe;*.lnk|Tous les fichiers (*.*)|*.*",
            DereferenceLinks = false,
        };
        var currentDirectory = GetExistingDirectory(assistantSettingsRow.ApplicationTarget);
        applicationDialog.InitialDirectory = currentDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        if (applicationDialog.ShowDialog(this) == true) assistantSettingsRow.ApplicationTarget = applicationDialog.FileName;
    }

    /// <summary>Returns the folder of a file path when it exists, or null for a bare command or an invalid path.</summary>
    private static string? GetExistingDirectory(string filePath)
    {
        try
        {
            var directoryPath = Path.GetDirectoryName(filePath);
            return !string.IsNullOrEmpty(directoryPath) && Directory.Exists(directoryPath) ? directoryPath : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
