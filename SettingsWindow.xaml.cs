using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DevLauncher.Services;

namespace DevLauncher;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        LoadSettings();
    }

    // ════════════════════════════════════════════════════════
    //  CHARGEMENT
    // ════════════════════════════════════════════════════════

    private void LoadSettings()
    {
        // XAMPP
        HtdocsBox.Text = AppSettings.HtdocsPath;
        XamppDirBox.Text = AppSettings.XamppDir;
        ApacheExeBox.Text = AppSettings.ApacheExe;
        MySQLExeBox.Text = AppSettings.MySQLExe;
        MySQLConfigBox.Text = AppSettings.MySQLConfig;
        FileZillaExeBox.Text = AppSettings.FileZillaExe;
        XamppPanelBox.Text = AppSettings.XamppPanel;

        // Mercure
        MercureDirBox.Text = AppSettings.MercureDir;

        // Éditeurs
        VSCodeBox.Text = AppSettings.VSCodeExecutable;
        VisualStudioBox.Text = AppSettings.VisualStudioExecutable;

        // Navigateurs
        ChromeBox.Text = AppSettings.ChromeExe;
        FirefoxBox.Text = AppSettings.FirefoxExe;

        // Symfony
        SymfonyPortBox.Text = AppSettings.SymfonyPort.ToString();
    }

    // ════════════════════════════════════════════════════════
    //  SAUVEGARDE
    // ════════════════════════════════════════════════════════

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Valider le port Symfony
        if (!int.TryParse(SymfonyPortBox.Text, out int port) || port < 1 || port > 65535)
        {
            StatusText.Text = "❌ Port Symfony invalide (1-65535)";
            StatusText.Foreground = System.Windows.Media.Brushes.Red;
            return;
        }

        // Appliquer dans AppSettings
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
        AppSettings.SymfonyPort = port;

        // Sauvegarder dans un fichier JSON
        SettingsService.Save();

        StatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
        StatusText.Text = "✅ Paramètres sauvegardés";

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;

    // ════════════════════════════════════════════════════════
    //  PARCOURIR
    // ════════════════════════════════════════════════════════

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        // Trouver le TextBox associé dans le même Grid
        var grid = btn.Parent as System.Windows.Controls.Grid;
        var textBox = grid?.Children.OfType<System.Windows.Controls.TextBox>()
                          .FirstOrDefault();
        if (textBox is null) return;

        var isFolder = btn.Tag as string == "Folder";

        if (isFolder)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Sélectionne un dossier",
                InitialDirectory = textBox.Text,
            };
            if (dialog.ShowDialog() == true)
                textBox.Text = dialog.FolderName;
        }
        else
        {
            var dialog = new OpenFileDialog
            {
                Title = "Sélectionne un fichier",
                Filter = "Exécutables (*.exe)|*.exe|Tous les fichiers (*.*)|*.*",
                InitialDirectory = Path.GetDirectoryName(textBox.Text),
            };
            if (dialog.ShowDialog() == true)
                textBox.Text = dialog.FileName;
        }
    }
}