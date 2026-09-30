using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using DevLauncher.Models;
using DevLauncher.Services;
using DevLauncher.Views;

namespace DevLauncher;

public partial class MainWindow : Window
{
    private const string NewProfileEntry = "+ Nouveau profil...";
    private const string DefaultLaunchButtonLabel = "▶ Lancer l'environnement";
    private const string RecentProjectsGroupName = "⭐ Récents";
    private const string AllProjectsGroupName = "📁 Tous les projets";

    // ── Services ──────────────────────────────────────────────
    private readonly ProjectScanner _projectScanner = new();
    private readonly ProfileService _profileService = new();
    private readonly RecentProjectsService _recentProjectsService = new();
    private readonly ProcessEventWatcher _processEventWatcher = new();
    private readonly ServiceMonitor _serviceMonitor;
    private readonly LaunchService _launchService;

    // ── State ─────────────────────────────────────────────────
    private List<ProjectListEntry> _projectEntries = new();
    private string? _selectedProjectPath;
    private string? _activeProfileName;
    private bool _isUpdatingProfileList;
    private bool _isLaunchInProgress;

    private string? SelectedProjectName => _selectedProjectPath is null ? null : Path.GetFileName(_selectedProjectPath);

    public MainWindow()
    {
        InitializeComponent();
        LogRichText.Document.Blocks.Clear();
        _serviceMonitor = new ServiceMonitor(_processEventWatcher);
        _launchService = new LaunchService(_processEventWatcher);
        _launchService.LogMessage += message => AppendLog(message, isError: false);
        _launchService.LogError += message => AppendLog(message, isError: true);
        _serviceMonitor.StatusChanged += OnServiceStatusChanged;
        if (!_processEventWatcher.Start())
            AppendLog("⚠️ Surveillance des processus indisponible (droits administrateur requis) : indicateurs mis à jour uniquement au lancement et à l'arrêt", isError: true);
        _serviceMonitor.RefreshStatus();
        LoadMercureScripts();
        RefreshProjectList();
    }

    // ════════════════════════════════════════════════════════════
    //  PROJECTS
    // ════════════════════════════════════════════════════════════

    private void RefreshProjectList()
    {
        var projectPaths = _projectScanner.GetProjects();
        _projectEntries = BuildProjectEntries(projectPaths);
        if (_selectedProjectPath is not null && !_projectEntries.Any(entry => IsSamePath(entry.Path, _selectedProjectPath)))
            ClearProjectSelection();
        ApplyProjectFilter();
        if (Directory.Exists(AppSettings.HtdocsPath))
            Log($"📁 {projectPaths.Count} projet(s) trouvé(s) dans {AppSettings.HtdocsPath}");
        else
            AppendLog($"❌ Dossier des projets introuvable : {AppSettings.HtdocsPath}", isError: true);
    }

    /// <summary>
    /// Rebuilds the list entries from the scanned projects : recently launched projects first, then the others alphabetically.
    /// The recent projects only change after a launch, never during a click, so that the list does not move under the cursor.
    /// </summary>
    private List<ProjectListEntry> BuildProjectEntries(IReadOnlyCollection<string> projectPaths)
    {
        var recentProjectPaths = _recentProjectsService.GetRecentProjectPaths().Where(Directory.Exists).ToList();
        var recentProjectEntries = recentProjectPaths
            .Select(projectPath => new ProjectListEntry(Path.GetFileName(projectPath), projectPath, RecentProjectsGroupName));
        var otherProjectEntries = projectPaths
            .Where(projectPath => !recentProjectPaths.Any(recentProjectPath => IsSamePath(recentProjectPath, projectPath)))
            .Select(projectPath => new ProjectListEntry(Path.GetFileName(projectPath), projectPath, AllProjectsGroupName));
        return recentProjectEntries.Concat(otherProjectEntries).ToList();
    }

    /// <summary>Filters the projects list by the search text and keeps the selected project highlighted.</summary>
    private void ApplyProjectFilter()
    {
        var searchText = SearchBox.Text;
        var visibleProjectEntries = _projectEntries
            .Where(entry => string.IsNullOrWhiteSpace(searchText) || entry.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var groupedProjectEntries = new ListCollectionView(visibleProjectEntries);
        groupedProjectEntries.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ProjectListEntry.GroupName)));
        ProjectListBox.ItemsSource = groupedProjectEntries;
        ProjectListBox.SelectedItem = visibleProjectEntries.FirstOrDefault(entry => _selectedProjectPath is not null && IsSamePath(entry.Path, _selectedProjectPath));
        if (ProjectListBox.SelectedItem is not null) ProjectListBox.ScrollIntoView(ProjectListBox.SelectedItem);
    }

    private static bool IsSamePath(string firstPath, string secondPath)
        => string.Equals(Path.TrimEndingDirectorySeparator(firstPath), Path.TrimEndingDirectorySeparator(secondPath), StringComparison.OrdinalIgnoreCase);

    private void LoadMercureScripts()
    {
        var mercureScriptNames = Directory.Exists(AppSettings.MercureDir)
            ? Directory.GetFiles(AppSettings.MercureDir, "start*.ps1").Select(Path.GetFileName).ToList()
            : new List<string?>();
        MercureScriptCombo.ItemsSource = mercureScriptNames;
        MercureScriptCombo.SelectedIndex = mercureScriptNames.Count > 0 ? 0 : -1;
    }

    private void RefreshProjects_Click(object sender, RoutedEventArgs e) => RefreshProjectList();

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyProjectFilter();

    private void ProjectListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // A filtered-out project stays selected : only an explicit choice of another project changes it.
        if (ProjectListBox.SelectedItem is not ProjectListEntry projectEntry) return;
        if (_selectedProjectPath is not null && IsSamePath(projectEntry.Path, _selectedProjectPath)) return;
        SelectProject(projectEntry.Path);
    }

    private void SelectProject(string projectPath)
    {
        var projectName = Path.GetFileName(projectPath);
        var projectDetection = _projectScanner.DetectProject(projectPath);
        _selectedProjectPath = projectPath;
        SelectedPathText.Text = projectPath;
        AutoDetectBadge.Visibility = projectDetection.IsSymfony ? Visibility.Visible : Visibility.Collapsed;
        if (projectDetection.IsSymfony) Log($"✅ Symfony détecté automatiquement dans « {projectName} »");
        LoadProfilesForProject(projectName, projectDetection);
        StatusText.Text = $"Prêt à lancer : {projectName}";
        RefreshActionButtons();
    }

    private void ClearProjectSelection()
    {
        _selectedProjectPath = null;
        _activeProfileName = null;
        SelectedPathText.Text = "Aucun projet sélectionné";
        AutoDetectBadge.Visibility = Visibility.Collapsed;
        StatusText.Text = "Sélectionne un projet pour commencer";
        UpdateProfileList(Array.Empty<string>(), null);
        RefreshActionButtons();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow { Owner = this };
        if (settingsWindow.ShowDialog() != true) return;
        Log("⚙️ Paramètres mis à jour");
        LoadMercureScripts();
        RefreshProjectList();
    }

    // ════════════════════════════════════════════════════════════
    //  OPTIONS PANEL
    // ════════════════════════════════════════════════════════════

    private void ProjectType_Changed(object sender, RoutedEventArgs e)
    {
        if (SymfonyOptionsCard is null) return;
        SymfonyOptionsCard.Visibility = RadioSymfony.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ChkBrowser_Changed(object sender, RoutedEventArgs e)
    {
        if (BrowserPanel is null) return;
        BrowserPanel.Visibility = ChkBrowser.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ChkMercure_Changed(object sender, RoutedEventArgs e)
    {
        if (MercurePanel is null) return;
        MercurePanel.Visibility = ChkMercure.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Displays a profile in the options panel.</summary>
    private void ApplyProfile(ProjectProfile profile)
    {
        RadioSymfony.IsChecked = profile.IsSymfony;
        RadioOther.IsChecked = !profile.IsSymfony;
        RadioVSCode.IsChecked = profile.OpenVSCode;
        RadioVisualStudio.IsChecked = profile.OpenVisualStudio;
        RadioNoEditor.IsChecked = !profile.OpenVSCode && !profile.OpenVisualStudio;
        ChkXamppPanel.IsChecked = profile.ShowXamppPanel;
        ChkApache.IsChecked = profile.StartApache;
        ChkMySQL.IsChecked = profile.StartMySQL;
        ChkFileZilla.IsChecked = profile.StartFileZilla;
        ChkSymfonyServer.IsChecked = profile.StartSymfonyServer;
        ChkTailwind.IsChecked = profile.StartTailwind;
        ChkMercure.IsChecked = profile.StartMercure;
        if (profile.MercureScript is not null && MercureScriptCombo.Items.Contains(profile.MercureScript))
            MercureScriptCombo.SelectedItem = profile.MercureScript;
        ChkTerminal.IsChecked = profile.OpenTerminal;
        ChkBrowser.IsChecked = profile.OpenBrowser;
        ChkBrowserDefault.IsChecked = profile.BrowserDefault;
        ChkBrowserChrome.IsChecked = profile.BrowserChrome;
        ChkBrowserFirefox.IsChecked = profile.BrowserFirefox;
    }

    /// <summary>Captures the options panel as a profile. This profile is also the launch request.</summary>
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

    // ════════════════════════════════════════════════════════════
    //  LAUNCH / STOP
    // ════════════════════════════════════════════════════════════

    private async void Launch_Click(object sender, RoutedEventArgs e) => await LaunchSelectedProjectAsync();

    private async Task LaunchSelectedProjectAsync()
    {
        if (_selectedProjectPath is null || _isLaunchInProgress) return;
        var projectName = SelectedProjectName;
        SetLaunchInProgress(true);
        StatusText.Text = "⏳ Lancement en cours…";
        Log("═══════════════════════════════");
        Log($"🚀 Lancement de « {projectName} »");
        RegisterRecentProject(_selectedProjectPath);
        try
        {
            await _launchService.LaunchAsync(_selectedProjectPath, CaptureCurrentOptions(_activeProfileName ?? ProjectProfile.DefaultProfileName));
            StatusText.Text = $"✅ Environnement lancé — {projectName}";
            Log("✅ Lancement terminé");
        }
        catch (Exception exception)
        {
            StatusText.Text = "❌ Erreur lors du lancement";
            AppendLog($"❌ Erreur : {exception.Message}", isError: true);
        }
        finally
        {
            SetLaunchInProgress(false);
            _serviceMonitor.RefreshStatus();
        }
    }

    private async void StopAll_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = MessageBox.Show(
            "Arrêter les services et fermer les éditeurs lancés par DevLauncher ?",
            "⏹ Tout arrêter",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes) return;
        StopAllButton.IsEnabled = false;
        Log("⏹ Arrêt de l'environnement…");
        try
        {
            await _launchService.StopAllAsync();
            StatusText.Text = "⏹ Environnement arrêté";
        }
        catch (Exception exception)
        {
            AppendLog($"❌ Erreur lors de l'arrêt : {exception.Message}", isError: true);
        }
        finally
        {
            StopAllButton.IsEnabled = true;
            _serviceMonitor.RefreshStatus();
        }
    }

    /// <summary>Moves the launched project to the recent projects section, keeping it selected.</summary>
    private void RegisterRecentProject(string projectPath)
    {
        _recentProjectsService.RegisterLaunch(projectPath);
        _projectEntries = BuildProjectEntries(_projectScanner.GetProjects());
        ApplyProjectFilter();
    }

    private void SetLaunchInProgress(bool isLaunchInProgress)
    {
        _isLaunchInProgress = isLaunchInProgress;
        LaunchProfilesPopup.IsOpen = false;
        RefreshActionButtons();
    }

    private void LaunchDropdown_Click(object sender, RoutedEventArgs e) => LaunchProfilesPopup.IsOpen = true;

    /// <summary>Rebuilds the launch buttons : split button with a profiles menu when the project has several profiles.</summary>
    private void RefreshLaunchButtons()
    {
        var profileNames = GetProfileNames();
        var hasSeveralProfiles = _selectedProjectPath is not null && profileNames.Count > 1;
        LaunchButton.IsEnabled = _selectedProjectPath is not null && !_isLaunchInProgress;
        LaunchDropdownButton.IsEnabled = LaunchButton.IsEnabled;
        LaunchSplitSeparator.Visibility = hasSeveralProfiles ? Visibility.Visible : Visibility.Collapsed;
        LaunchDropdownButton.Visibility = hasSeveralProfiles ? Visibility.Visible : Visibility.Collapsed;
        LaunchButton.Tag = hasSeveralProfiles ? new CornerRadius(8, 0, 0, 8) : new CornerRadius(8);
        LaunchButton.Content = hasSeveralProfiles ? $"▶ {_activeProfileName}" : DefaultLaunchButtonLabel;
        LaunchProfilesPanel.Children.Clear();
        if (!hasSeveralProfiles) return;
        foreach (var profileName in profileNames)
        {
            var profileLaunchButton = new Button { Content = profileName, Style = (Style)FindResource("PopupProfileButton") };
            profileLaunchButton.Click += async (_, _) =>
            {
                LaunchProfilesPopup.IsOpen = false;
                ProfileComboBox.SelectedItem = profileName;
                await LaunchSelectedProjectAsync();
            };
            LaunchProfilesPanel.Children.Add(profileLaunchButton);
        }
    }

    // ════════════════════════════════════════════════════════════
    //  PROFILES
    // ════════════════════════════════════════════════════════════

    private void LoadProfilesForProject(string projectName, ProjectDetection projectDetection)
    {
        var profiles = _profileService.GetProfiles(projectName);
        if (profiles.Count == 0)
        {
            var defaultProfile = ProjectProfile.CreateDefault(projectDetection);
            _profileService.SaveProfile(projectName, defaultProfile);
            profiles.Add(defaultProfile);
        }
        var lastUsedProfileName = _profileService.GetLastUsedProfile(projectName);
        var profileToActivate = profiles.FirstOrDefault(profile => profile.Name == lastUsedProfileName) ?? profiles[0];
        UpdateProfileList(profiles.Select(profile => profile.Name), profileToActivate.Name);
        ActivateProfile(profileToActivate);
    }

    /// <summary>Fills the profiles dropdown without triggering the selection handler.</summary>
    private void UpdateProfileList(IEnumerable<string> profileNames, string? selectedProfileName)
    {
        _isUpdatingProfileList = true;
        try
        {
            ProfileComboBox.Items.Clear();
            foreach (var profileName in profileNames) ProfileComboBox.Items.Add(profileName);
            if (_selectedProjectPath is not null) ProfileComboBox.Items.Add(NewProfileEntry);
            ProfileComboBox.SelectedItem = selectedProfileName;
        }
        finally
        {
            _isUpdatingProfileList = false;
        }
    }

    private void SelectProfileInList(string? profileName)
    {
        _isUpdatingProfileList = true;
        try { ProfileComboBox.SelectedItem = profileName; }
        finally { _isUpdatingProfileList = false; }
    }

    private List<string> GetProfileNames()
        => ProfileComboBox.Items.Cast<string>().Where(profileName => profileName != NewProfileEntry).ToList();

    /// <summary>Displays a profile and makes it the active one.</summary>
    private void ActivateProfile(ProjectProfile profile)
    {
        ApplyProfile(profile);
        SetActiveProfile(profile.Name);
        Log($"📂 Profil « {profile.Name} » chargé");
    }

    /// <summary>Makes a profile the active one of the selected project, without changing the options panel.</summary>
    private void SetActiveProfile(string profileName)
    {
        _activeProfileName = profileName;
        if (SelectedProjectName is { } projectName) _profileService.SaveLastUsedProfile(projectName, profileName);
        RefreshActionButtons();
    }

    private void RefreshActionButtons()
    {
        var hasProject = _selectedProjectPath is not null;
        ProfileComboBox.IsEnabled = hasProject;
        SaveProfileButton.IsEnabled = hasProject;
        RenameProfileButton.IsEnabled = hasProject;
        DeleteProfileButton.IsEnabled = hasProject && GetProfileNames().Count > 1;
        RefreshLaunchButtons();
    }

    private void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingProfileList || ProfileComboBox.SelectedItem is not string profileName || SelectedProjectName is not { } projectName) return;
        if (profileName == NewProfileEntry)
        {
            CreateProfile();
            return;
        }
        var profile = _profileService.GetProfile(projectName, profileName);
        if (profile is not null) ActivateProfile(profile);
    }

    private void CreateProfile()
    {
        var profileName = AskProfileName("Nouveau profil", "Créer", string.Empty);
        if (profileName is null || SelectedProjectName is not { } projectName)
        {
            SelectProfileInList(_activeProfileName);
            return;
        }
        _profileService.SaveProfile(projectName, CaptureCurrentOptions(profileName));
        UpdateProfileList(GetProfileNames().Append(profileName), profileName);
        SetActiveProfile(profileName);
        Log($"✨ Nouveau profil « {profileName} » créé");
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProjectName is not { } projectName || _activeProfileName is null) return;
        _profileService.SaveProfile(projectName, CaptureCurrentOptions(_activeProfileName));
        Log($"💾 Profil « {_activeProfileName} » sauvegardé");
        StatusText.Text = $"✅ Profil « {_activeProfileName} » sauvegardé";
    }

    private void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProjectName is not { } projectName || _activeProfileName is not { } currentProfileName) return;
        var newProfileName = AskProfileName("Renommer le profil", "Renommer", currentProfileName);
        if (newProfileName is null || newProfileName == currentProfileName) return;
        _profileService.RenameProfile(projectName, currentProfileName, newProfileName);
        UpdateProfileList(GetProfileNames().Select(profileName => profileName == currentProfileName ? newProfileName : profileName), newProfileName);
        SetActiveProfile(newProfileName);
        Log($"✏️ Profil « {currentProfileName} » renommé en « {newProfileName} »");
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProjectName is not { } projectName || _activeProfileName is not { } profileNameToDelete) return;
        var confirmation = MessageBox.Show($"Supprimer le profil « {profileNameToDelete} » ?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes) return;
        _profileService.DeleteProfile(projectName, profileNameToDelete);
        Log($"🗑️ Profil « {profileNameToDelete} » supprimé");
        LoadProfilesForProject(projectName, _projectScanner.DetectProject(_selectedProjectPath!));
    }

    /// <summary>Asks for a profile name until it is unique. Returns null when cancelled.</summary>
    private string? AskProfileName(string dialogTitle, string confirmLabel, string initialProfileName)
    {
        var existingProfileNames = GetProfileNames();
        while (true)
        {
            var profileNameDialog = new ProfileNameDialog(dialogTitle, confirmLabel, initialProfileName) { Owner = this };
            if (profileNameDialog.ShowDialog() != true) return null;
            var profileName = profileNameDialog.ProfileName;
            var isUnchangedName = profileName == initialProfileName;
            if (profileName != NewProfileEntry && (isUnchangedName || !existingProfileNames.Contains(profileName))) return profileName;
            MessageBox.Show($"Un profil « {profileName} » existe déjà.", "Nom existant", MessageBoxButton.OK, MessageBoxImage.Warning);
            initialProfileName = profileName;
        }
    }

    // ════════════════════════════════════════════════════════════
    //  LOGS AND STATUS
    // ════════════════════════════════════════════════════════════

    private void Log(string message) => AppendLog(message, isError: false);

    /// <summary>Appends a timestamped line to the log. Can be called from any thread.</summary>
    private void AppendLog(string message, bool isError)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => AppendLog(message, isError));
            return;
        }
        var logParagraph = new Paragraph(new Run($"[{DateTime.Now:HH:mm:ss}] {message}"))
        {
            Foreground = (Brush)FindResource(isError ? "AccentRedBrush" : "TextSecondaryBrush"),
            Margin = new Thickness(0),
        };
        LogRichText.Document.Blocks.Add(logParagraph);
        LogRichText.ScrollToEnd();
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => LogRichText.Document.Blocks.Clear();

    private void OnServiceStatusChanged(string serviceName, bool isRunning)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnServiceStatusChanged(serviceName, isRunning));
            return;
        }
        var indicatorBrush = (Brush)FindResource(isRunning ? "AccentGreenBrush" : "AccentRedBrush");
        switch (serviceName)
        {
            case "Apache": ApacheIndicator.Fill = indicatorBrush; break;
            case "MySQL": MySqlIndicator.Fill = indicatorBrush; break;
            case "FileZilla": FileZillaIndicator.Fill = indicatorBrush; break;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _launchService.RestorePendingVSCodeTasks();
        _serviceMonitor.Dispose();
        _processEventWatcher.Dispose();
        base.OnClosed(e);
    }
}
