using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DevLauncher.Services;

namespace DevLauncher;

public partial class MainWindow : Window
{
    // ── Services ──────────────────────────────────────────────
    private readonly ProjectScanner  _scanner;
    private readonly LaunchService   _launcher;
    private readonly ServiceMonitor  _monitor;

    // ── État ──────────────────────────────────────────────────
    private List<string> _allProjects = new();
    private string?      _selectedProject;

    public MainWindow()
    {
        InitializeComponent();

        _scanner  = new ProjectScanner(AppSettings.HtdocsPath);
        _launcher = new LaunchService(AppSettings.XamppDir, AppSettings.MercureDir);
        _monitor  = new ServiceMonitor();

        // Écoute des logs du service de lancement
        _launcher.LogMessage += OnLogMessage;

        // Surveillance des services (Apache / MySQL) toutes les 3s
        _monitor.StatusChanged += OnServiceStatusChanged;
        _monitor.Start(TimeSpan.FromSeconds(3));

        LoadMercureScripts();
        RefreshProjectList();

        // Réagit à la case Mercure
        ChkMercure.Checked += (_, _) => { if (MercurePanel != null) MercurePanel.Visibility = Visibility.Visible; };
        ChkMercure.Unchecked += (_, _) => { if (MercurePanel != null) MercurePanel.Visibility = Visibility.Collapsed; };
    }

    // ════════════════════════════════════════════════════════════
    //  CHARGEMENT / RAFRAÎCHISSEMENT
    // ════════════════════════════════════════════════════════════

    private void RefreshProjectList()
    {
        _allProjects = _scanner.GetProjects();
        ApplyFilter(SearchBox.Text);
        Log($"📁 {_allProjects.Count} projet(s) trouvé(s) dans {AppSettings.HtdocsPath}");
    }

    private void ApplyFilter(string filter)
    {
        var filtered = string.IsNullOrWhiteSpace(filter)
            ? _allProjects
            : _allProjects.Where(p =>
                Path.GetFileName(p).Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        ProjectListBox.ItemsSource = filtered.Select(p => Path.GetFileName(p)).ToList();
    }

    private void LoadMercureScripts()
    {
        if (!Directory.Exists(AppSettings.MercureDir)) return;

        var scripts = Directory.GetFiles(AppSettings.MercureDir, "start*.ps1")
                               .Select(Path.GetFileName)
                               .ToList();

        MercureScriptCombo.ItemsSource   = scripts;
        MercureScriptCombo.SelectedIndex = scripts.Count > 0 ? 0 : -1;
    }

    // ════════════════════════════════════════════════════════════
    //  ÉVÉNEMENTS UI
    // ════════════════════════════════════════════════════════════

    private void RefreshProjects_Click(object sender, RoutedEventArgs e)
        => RefreshProjectList();

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        => ApplyFilter(SearchBox.Text);

    private void ProjectListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectListBox.SelectedItem is not string name) return;

        _selectedProject        = Path.Combine(AppSettings.HtdocsPath, name);
        SelectedPathText.Text   = _selectedProject;
        LaunchButton.IsEnabled  = true;
        StatusText.Text         = $"Prêt à lancer : {name}";

        // Détection automatique Symfony
        bool isSymfony = _scanner.IsSymfonyProject(_selectedProject);
        AutoDetectBadge.Visibility    = isSymfony ? Visibility.Visible : Visibility.Collapsed;
        SymfonyOptionsCard.Visibility = isSymfony ? Visibility.Visible : Visibility.Collapsed;

        if (isSymfony)
        {
            RadioSymfony.IsChecked = true;
            Log($"✅ Symfony détecté automatiquement dans « {name} »");
        }
        else
        {
            RadioOther.IsChecked = true;
        }
    }

    private void ProjectType_Changed(object sender, RoutedEventArgs e)
    {
        if (SymfonyOptionsCard == null) return;
        bool symfony = RadioSymfony.IsChecked == true;
        SymfonyOptionsCard.Visibility = symfony ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
        => LogTextBlock.Text = string.Empty;

    // ════════════════════════════════════════════════════════════
    //  LANCEMENT
    // ════════════════════════════════════════════════════════════

    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProject is null) return;

        LaunchButton.IsEnabled = false;
        StatusText.Text        = "⏳ Lancement en cours…";

        var options = BuildOptions();
        Log("═══════════════════════════════");
        Log($"🚀 Lancement de « {Path.GetFileName(_selectedProject)} »");

        try
        {
            await _launcher.LaunchAsync(_selectedProject, options);
            StatusText.Text = $"✅ Environnement lancé — {Path.GetFileName(_selectedProject)}";
            Log("✅ Tout est lancé !");
        }
        catch (Exception ex)
        {
            StatusText.Text = "❌ Erreur lors du lancement";
            Log($"❌ Erreur : {ex.Message}");
        }
        finally
        {
            LaunchButton.IsEnabled = true;
        }
    }

    private void StopAll_Click(object sender, RoutedEventArgs e)
    {
        Log("⏹ Arrêt des services XAMPP…");
        _launcher.StopAll();
        Log("✅ Services arrêtés.");
    }

    // ════════════════════════════════════════════════════════════
    //  CONSTRUCTION DES OPTIONS
    // ════════════════════════════════════════════════════════════

    private LaunchOptions BuildOptions() => new()
    {
        IsSymfony      = RadioSymfony.IsChecked         == true,
        OpenVSCode = RadioVSCode.IsChecked              == true,
        OpenVisualStudio = RadioVisualStudio.IsChecked  == true,
        OpenTerminal   = ChkTerminal.IsChecked          == true,
        OpenBrowser = ChkBrowser.IsChecked              == true,
        BrowserDefault = ChkBrowserDefault.IsChecked    == true,
        BrowserChrome = ChkBrowserChrome.IsChecked      == true,
        BrowserFirefox = ChkBrowserFirefox.IsChecked    == true,
        StartApache    = ChkApache.IsChecked            == true,
        StartMySQL     = ChkMySQL.IsChecked             == true,
        StartFileZilla = ChkFileZilla.IsChecked         == true,
        ShowXamppPanel = ChkXamppPanel.IsChecked        == true,

        // Symfony
        StartSymfonyServer = ChkSymfonyServer.IsChecked == true,
        StartTailwind      = ChkTailwind.IsChecked      == true,
        StartMercure       = ChkMercure.IsChecked       == true,
        MercureScript      = MercureScriptCombo.SelectedItem as string,
    };

    private void ChkBrowser_Checked(object sender, RoutedEventArgs e)
    {
        if (BrowserPanel == null) return;
        BrowserPanel.Visibility = ChkBrowser.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // ════════════════════════════════════════════════════════════
    //  LOGS ET STATUTS
    // ════════════════════════════════════════════════════════════

    private void OnLogMessage(string message)
    {
        // Peut être appelé depuis un thread de fond
        Dispatcher.Invoke(() => Log(message));
    }

    private void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        LogTextBlock.Text += $"[{timestamp}] {message}\n";

        // Auto-scroll
        LogScrollViewer.ScrollToBottom();
    }

    private void OnServiceStatusChanged(string service, bool isRunning)
    {
        Dispatcher.Invoke(() =>
        {
            var color = isRunning
                ? (SolidColorBrush)FindResource("AccentGreenBrush")
                : (SolidColorBrush)FindResource("AccentRedBrush");

            if (service == "Apache") ApacheIndicator.Fill = color;
            if (service == "MySQL")  MySqlIndicator.Fill  = color;
            if (service == "FileZilla") FileZillaIndicator.Fill = color;
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        _monitor.Stop();
        base.OnClosed(e);
    }
}
