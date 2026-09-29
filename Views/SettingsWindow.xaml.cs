using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DevLauncher.Services;
using Microsoft.Win32;

namespace DevLauncher.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        LoadSettings();
    }

    // ════════════════════════════════════════════════════════
    //  LOAD
    // ════════════════════════════════════════════════════════

    private void LoadSettings()
    {
        HtdocsBox.Text = AppSettings.HtdocsPath;
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
    }

    // ════════════════════════════════════════════════════════
    //  SAVE
    // ════════════════════════════════════════════════════════

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadPort(SymfonyPortBox, "Symfony", out var symfonyPort) || !TryReadPort(LocalWebPortBox, "Apache", out var localWebPort)) return;
        if (string.IsNullOrWhiteSpace(HtdocsBox.Text))
        {
            ShowValidationError("❌ Le dossier des projets est obligatoire");
            return;
        }
        AppSettings.HtdocsPath = HtdocsBox.Text.Trim();
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
