using DevLauncher.Models;
using DevLauncher.Services;
using DevLauncher.Models;
using DevLauncher.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DevLauncher;

public partial class MainWindow : Window
{
    // ── Services ──────────────────────────────────────────────
    private readonly ProjectScanner  _scanner;
    private readonly LaunchService   _launcher;
    private readonly ServiceMonitor  _monitor;
    private readonly ProfileService _profileService;

    // ── État ──────────────────────────────────────────────────
    private List<string> _allProjects = new();
    private string?      _selectedProject;
    private bool _isLoadingProfiles;

    public MainWindow()
    {
        InitializeComponent();

        _scanner  = new ProjectScanner(AppSettings.HtdocsPath);
        _launcher = new LaunchService(AppSettings.XamppDir, AppSettings.MercureDir);
        _monitor  = new ServiceMonitor();
        _profileService = new ProfileService();

        // Écoute des logs du service de lancement
        _launcher.LogMessage += OnLogMessage;
        _launcher.LogError += OnLogError;

        // Surveillance des services (Apache / MySQL) toutes les 3s
        _monitor.StatusChanged += OnServiceStatusChanged;
        _monitor.Start(TimeSpan.FromSeconds(3));

        LoadMercureScripts();
        RefreshProjectList();

        // Réagit à la case Mercure
        ChkMercure.Checked += (_, _) => { if (MercurePanel != null) MercurePanel.Visibility = Visibility.Visible; };
        ChkMercure.Unchecked += (_, _) => { if (MercurePanel != null) MercurePanel.Visibility = Visibility.Collapsed; };
        Loaded += MainWindow_Loaded;
        SettingsService.Load();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Rien ici pour l'instant, le chargement se fera à la sélection du projet
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow { Owner = this };
        if (window.ShowDialog() == true)
        {
            // Recharger la liste des projets si htdocs a changé
            RefreshProjectList();
            Log("⚙️ Paramètres mis à jour");
        }
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
        // Charger les profils du projet
        LoadProfilesForProject(name);

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

private async void StopAll_Click(object sender, RoutedEventArgs e)
{
    var result = MessageBox.Show(
        "Arrêter tous les services et fermer les éditeurs ?",
        "⏹ Tout arrêter",
        MessageBoxButton.YesNo,
        MessageBoxImage.Question);

    if (result != MessageBoxResult.Yes) return;

    Log("⏹ Arrêt de l'environnement…");
    await _launcher.StopAllAsync();
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
        => Dispatcher.Invoke(() => AppendLog(message, (SolidColorBrush)FindResource("TextSecondaryBrush")));

    private void OnLogError(string message)
        => Dispatcher.Invoke(() => AppendLog(message, (SolidColorBrush)FindResource("AccentRedBrush")));

    private void AppendLog(string message, SolidColorBrush color)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var para = new System.Windows.Documents.Paragraph(
            new System.Windows.Documents.Run($"[{timestamp}] {message}"))
        {
            Foreground = color,
            Margin = new Thickness(0),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11
        };
        LogRichText.Document.Blocks.Add(para);
        LogRichText.ScrollToEnd();
    }

    private void Log(string message)
        => Dispatcher.Invoke(() => AppendLog(message, (SolidColorBrush)FindResource("TextSecondaryBrush")));

    private void ClearLog_Click(object sender, RoutedEventArgs e)
        => LogRichText.Document.Blocks.Clear();

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

    // ════════════════════════════════════════════════════════
    //  PROFILS
    // ════════════════════════════════════════════════════════

    private void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileComboBox.SelectedItem is not string profileName) return;
        if (profileName == "+ Nouveau profil...") { AskNewProfileName(); return; }
        if (_selectedProject is null) return;

        var projectName = Path.GetFileName(_selectedProject);
        var profile = _profileService.GetProfile(projectName, profileName);
        if (profile is null) return;

        ApplyProfile(profile);
        DeleteProfileButton.IsEnabled = ProfileComboBox.Items.Count > 2;
        UpdateLaunchButton();

        // Mémoriser le profil sélectionné
        _profileService.SaveLastUsedProfile(projectName, profileName);
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProject is null) return;
        if (ProfileComboBox.SelectedItem is not string profileName) return;
        if (profileName == "+ Nouveau profil...") { AskNewProfileName(); return; }

        var projectName = Path.GetFileName(_selectedProject);
        var profile = CaptureCurrentOptions(profileName);
        _profileService.SaveProfile(projectName, profile);

        Log($"💾 Profil « {profileName} » sauvegardé");
        StatusText.Text = $"✅ Profil « {profileName} » sauvegardé";
    }

    private void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProject is null) return;
        if (ProfileComboBox.SelectedItem is not string oldName) return;
        if (oldName == "+ Nouveau profil...") return;

        var dialog = new ProfileNameDialog { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.ProfileName))
            return;

        var newName = dialog.ProfileName.Trim();
        var projectName = Path.GetFileName(_selectedProject);

        // Vérifier que le nom n'existe pas déjà
        if (ProfileComboBox.Items.Cast<string>().Any(p => p == newName))
        {
            MessageBox.Show($"Un profil « {newName} » existe déjà.", "Nom existant",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Charger l'ancien profil, changer son nom, sauvegarder
        var profile = _profileService.GetProfile(projectName, oldName);
        if (profile is null) return;

        profile.Name = newName;
        _profileService.SaveProfile(projectName, profile);
        _profileService.DeleteProfile(projectName, oldName);

        // Mettre à jour le ComboBox
        var index = ProfileComboBox.Items.IndexOf(oldName);
        ProfileComboBox.Items[index] = newName;
        ProfileComboBox.SelectedIndex = index;

        Log($"✏️ Profil « {oldName} » renommé en « {newName} »");
        UpdateLaunchButton();
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProject is null) return;
        if (ProfileComboBox.SelectedItem is not string profileName) return;
        if (profileName == "+ Nouveau profil...") return;

        var result = MessageBox.Show(
            $"Supprimer le profil « {profileName} » ?",
            "Confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        var projectName = Path.GetFileName(_selectedProject);
        _profileService.DeleteProfile(projectName, profileName);
        ProfileComboBox.Items.Remove(profileName);
        ProfileComboBox.SelectedIndex = 0;
        DeleteProfileButton.IsEnabled = ProfileComboBox.Items.Count > 2;
        Log($"🗑️ Profil « {profileName} » supprimé");
        UpdateLaunchButton();
    }

    private void LaunchDropdown_Click(object sender, RoutedEventArgs e)
    {
        LaunchProfilesPopup.PlacementTarget = LaunchDropdownButton;
        LaunchProfilesPopup.IsOpen = true;
    }

    private void AskNewProfileName()
    {
        var dialog = new ProfileNameDialog();
        dialog.Owner = this;

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.ProfileName))
        {
            if (ProfileComboBox.Items.Count > 1)
                ProfileComboBox.SelectedIndex = 0;
            return;
        }

        var name = dialog.ProfileName.Trim();

        // Vérifier que le nom n'existe pas déjà
        if (ProfileComboBox.Items.Cast<string>().Any(p => p == name))
        {
            MessageBox.Show($"Un profil « {name} » existe déjà.", "Nom existant",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Sauvegarder immédiatement en JSON avec les options actuelles
        if (_selectedProject is not null)
        {
            var projectName = Path.GetFileName(_selectedProject);
            var profile = CaptureCurrentOptions(name);
            _profileService.SaveProfile(projectName, profile);
        }

        // Insérer avant "+ Nouveau profil..."
        var insertIndex = ProfileComboBox.Items.Count - 1;
        ProfileComboBox.Items.Insert(insertIndex, name);
        ProfileComboBox.SelectedIndex = insertIndex;

        DeleteProfileButton.IsEnabled = ProfileComboBox.Items.Count > 2;
        Log($"✨ Nouveau profil « {name} » créé");
        UpdateLaunchButton();
    }

    private void UpdateLaunchButton()
    {
        if (_selectedProject is null) return;

        // Compter les vrais profils (sans "+ Nouveau profil...")
        var profileCount = ProfileComboBox.Items.Cast<string>()
            .Count(p => p != "+ Nouveau profil...");

        if (profileCount > 1)
        {
            // Afficher le bouton splitté
            LaunchSplitSeparator.Visibility = Visibility.Visible;
            LaunchDropdownBorder.Visibility = Visibility.Visible;

            // Mettre à jour le texte du bouton avec le profil sélectionné
            var selected = ProfileComboBox.SelectedItem as string;
            LaunchButton.Content = $"▶ {selected}";

            // Mettre à jour le popup
            RefreshLaunchPopup();
        }
        else
        {
            // Bouton simple
            LaunchSplitSeparator.Visibility = Visibility.Collapsed;
            LaunchDropdownBorder.Visibility = Visibility.Collapsed;
            LaunchButton.Content = "▶ Lancer l'environnement";
        }
    }

    private void RefreshLaunchPopup()
    {
        LaunchProfilesPanel.Children.Clear();

        foreach (var item in ProfileComboBox.Items.Cast<string>()
                 .Where(p => p != "+ Nouveau profil..."))
        {
            var profileName = item; // capture pour le lambda
            var btn = new Button
            {
                Content = profileName,
                Style = (Style)FindResource("PopupProfileButton"),
            };
            btn.Click += (_, _) =>
            {
                ProfileComboBox.SelectedItem = profileName;
                LaunchProfilesPopup.IsOpen = false;
                LaunchButton.Content = $"▶ {profileName}";
                Launch_Click(btn, new RoutedEventArgs());
            };
            LaunchProfilesPanel.Children.Add(btn);
        }
    }

    private void LoadProfilesForProject(string projectName)
    {
        ProfileComboBox.Items.Clear();

        var profiles = _profileService.GetProfiles(projectName);

        // Si aucun profil → créer un profil Défaut automatiquement
        if (profiles.Count == 0)
        {
            var defaultProfile = new ProjectProfile { Name = "Défaut" };
            _profileService.SaveProfile(projectName, defaultProfile);
            profiles.Add(defaultProfile);
        }

        foreach (var p in profiles)
            ProfileComboBox.Items.Add(p.Name);

        ProfileComboBox.Items.Add("+ Nouveau profil...");
        // Restaurer le dernier profil utilisé
        var lastUsed = _profileService.GetLastUsedProfile(projectName);
        var lastIndex = lastUsed != null
            ? ProfileComboBox.Items.Cast<string>()
                .ToList().IndexOf(lastUsed)
            : 0;
        ProfileComboBox.SelectedIndex   = lastIndex >= 0 ? lastIndex : 0;
        ProfileComboBox.IsEnabled       = true;
        SaveProfileButton.IsEnabled     = true;
        RenameProfileButton.IsEnabled   = true;
        DeleteProfileButton.IsEnabled   = profiles.Count > 1;

        // Charger le premier profil dans l'UI
        ApplyProfile(profiles[0]);
        UpdateLaunchButton();

        _isLoadingProfiles = false;

        // Appliquer le profil après que l'UI soit stable
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var profiles = _profileService.GetProfiles(projectName);
            if (profiles.Count == 0) return;

            var lastUsed = _profileService.GetLastUsedProfile(projectName);
            var toApply = profiles.FirstOrDefault(p => p.Name == lastUsed)
                           ?? profiles[0];

            ApplyProfile(toApply);

            // Sélectionner le bon item dans le ComboBox
            var index = ProfileComboBox.Items.Cast<string>()
                .ToList().IndexOf(toApply.Name);
            if (index >= 0) ProfileComboBox.SelectedIndex = index;

        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Applique un profil sauvegardé dans l'UI.</summary>
    private void ApplyProfile(ProjectProfile p)
    {
        // Type de projet
        RadioSymfony.IsChecked = p.IsSymfony;
        RadioOther.IsChecked = !p.IsSymfony;

        // Éditeur
        RadioVSCode.IsChecked = p.OpenVSCode;
        RadioVisualStudio.IsChecked = p.OpenVisualStudio;
        RadioNoEditor.IsChecked = !p.OpenVSCode && !p.OpenVisualStudio;

        // XAMPP
        ChkXamppPanel.IsChecked = p.ShowXamppPanel;
        ChkApache.IsChecked = p.StartApache;
        ChkMySQL.IsChecked = p.StartMySQL;
        ChkFileZilla.IsChecked = p.StartFileZilla;

        // Symfony
        ChkSymfonyServer.IsChecked = p.StartSymfonyServer;
        ChkTailwind.IsChecked = p.StartTailwind;
        ChkMercure.IsChecked = p.StartMercure;

        if (p.MercureScript != null)
            MercureScriptCombo.SelectedItem = p.MercureScript;

        // Outils
        ChkTerminal.IsChecked = p.OpenTerminal;
        ChkBrowser.IsChecked = p.OpenBrowser;
        ChkBrowserDefault.IsChecked = p.BrowserDefault;
        ChkBrowserChrome.IsChecked = p.BrowserChrome;
        ChkBrowserFirefox.IsChecked = p.BrowserFirefox;

        Log($"📂 Profil « {p.Name} » chargé");
    }

    /// <summary>Capture les options actuelles de l'UI dans un profil.</summary>
    private ProjectProfile CaptureCurrentOptions(string profileName) => new()
    {
        Name = profileName,
        IsSymfony = RadioSymfony.IsChecked == true,
        OpenVSCode = RadioVSCode.IsChecked == true,
        OpenVisualStudio = RadioVisualStudio.IsChecked == true,
        ShowXamppPanel = ChkXamppPanel.IsChecked == true,
        StartApache = ChkApache.IsChecked == true,
        StartMySQL = ChkMySQL.IsChecked == true,
        StartFileZilla = ChkFileZilla.IsChecked == true,
        StartSymfonyServer = ChkSymfonyServer.IsChecked == true,
        StartTailwind = ChkTailwind.IsChecked == true,
        StartMercure = ChkMercure.IsChecked == true,
        MercureScript = MercureScriptCombo.SelectedItem as string,
        OpenTerminal = ChkTerminal.IsChecked == true,
        OpenBrowser = ChkBrowser.IsChecked == true,
        BrowserDefault = ChkBrowserDefault.IsChecked == true,
        BrowserChrome = ChkBrowserChrome.IsChecked == true,
        BrowserFirefox = ChkBrowserFirefox.IsChecked == true,
    };

    protected override void OnClosed(EventArgs e)
    {
        _monitor.Stop();
        base.OnClosed(e);
    }
}
