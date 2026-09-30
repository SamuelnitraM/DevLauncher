using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevLauncher.Models;
using DevLauncher.Services;
using DevLauncher.Services.Tools;

namespace DevLauncher.ViewModels;

/// <summary>
/// State and actions of the main window : projects list, options panel built from the tool catalog,
/// profiles, launch and stop, service indicators and launch log.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private const string RecentProjectsGroupName = "⭐ Récents";
    private const string AllProjectsGroupName = "📁 Tous les projets";
    private const string DefaultLaunchButtonLabel = "▶ Lancer l'environnement";
    private const string NoProjectStatus = "Sélectionne un projet pour commencer";
    private const int MaximumLogEntryCount = 2000;

    private readonly ProjectScanner _projectScanner;
    private readonly ProfileService _profileService;
    private readonly RecentProjectsService _recentProjectsService;
    private readonly LaunchService _launchService;
    private readonly ServiceMonitor _serviceMonitor;
    private readonly ProcessEventWatcher _processEventWatcher;
    private readonly LaunchLog _launchLog;
    private readonly IUserInteractionService _userInteractionService;
    private readonly Dispatcher _uiDispatcher;

    private List<ProjectListEntry> _projectEntries = new();
    private bool _isUpdatingProfileList;

    public MainViewModel(
        ProjectScanner projectScanner,
        ProfileService profileService,
        RecentProjectsService recentProjectsService,
        ToolCatalog toolCatalog,
        LaunchService launchService,
        ServiceMonitor serviceMonitor,
        ProcessEventWatcher processEventWatcher,
        LaunchLog launchLog,
        IUserInteractionService userInteractionService)
    {
        _projectScanner = projectScanner;
        _profileService = profileService;
        _recentProjectsService = recentProjectsService;
        _launchService = launchService;
        _serviceMonitor = serviceMonitor;
        _processEventWatcher = processEventWatcher;
        _launchLog = launchLog;
        _userInteractionService = userInteractionService;
        _uiDispatcher = Dispatcher.CurrentDispatcher;
        ProjectsView = new ListCollectionView(VisibleProjects);
        ProjectsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ProjectListEntry.GroupName)));
        foreach (var toolCategory in ToolCategories.All)
            ToolCategoryViewModels.Add(new ToolCategoryViewModel(toolCategory, toolCatalog.GetCategoryTools(toolCategory)));
        foreach (var serviceName in new[] { "Apache", "MySQL", "FileZilla" })
            ServiceIndicators.Add(new ServiceIndicatorViewModel(serviceName));
        _launchLog.MessageLogged += OnMessageLogged;
        _serviceMonitor.StatusChanged += OnServiceStatusChanged;
        if (!_processEventWatcher.Start())
            _launchLog.Error("⚠️ Surveillance des processus indisponible (droits administrateur requis) : indicateurs mis à jour uniquement au lancement et à l'arrêt");
        _serviceMonitor.RefreshStatus();
        UpdateToolAvailability();
        RefreshProjects();
    }

    // ════════════════════════════════════════════════════════════
    //  BOUND STATE
    // ════════════════════════════════════════════════════════════

    public ObservableCollection<ProjectListEntry> VisibleProjects { get; } = new();
    public ListCollectionView ProjectsView { get; }
    public ObservableCollection<ToolCategoryViewModel> ToolCategoryViewModels { get; } = new();
    public ObservableCollection<string> ProfileNames { get; } = new();
    public ObservableCollection<ServiceIndicatorViewModel> ServiceIndicators { get; } = new();
    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    public IReadOnlyList<ProjectTypeOption> ProjectTypeOptions { get; } = new[]
    {
        new ProjectTypeOption(ProjectType.Symfony, "⚡ Symfony"),
        new ProjectTypeOption(ProjectType.Other, "📦 Autre (PHP / HTML…)"),
    };

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Item selected in the list. Becomes null when the current project is filtered out, without deselecting it.</summary>
    [ObservableProperty]
    private ProjectListEntry? _selectedProjectEntry;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProject), nameof(CurrentProjectDisplayPath), nameof(HasSeveralProfiles), nameof(LaunchButtonLabel))]
    private string? _currentProjectPath;

    [ObservableProperty]
    private bool _isSymfonyDetected;

    [ObservableProperty]
    private ProjectType _selectedProjectType;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LaunchButtonLabel))]
    private string? _activeProfileName;

    [ObservableProperty]
    private string _statusText = NoProjectStatus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isLaunchInProgress;

    [ObservableProperty]
    private bool _isStopInProgress;

    [ObservableProperty]
    private bool _isLaunchMenuOpen;

    public bool HasProject => CurrentProjectPath is not null;
    public bool IsIdle => !IsLaunchInProgress;
    public bool HasSeveralProfiles => HasProject && ProfileNames.Count > 1;
    public string CurrentProjectDisplayPath => CurrentProjectPath ?? "Aucun projet sélectionné";
    public string LaunchButtonLabel => HasSeveralProfiles ? $"▶ {ActiveProfileName}" : DefaultLaunchButtonLabel;
    private string? CurrentProjectName => CurrentProjectPath is null ? null : Path.GetFileName(CurrentProjectPath);

    partial void OnSearchTextChanged(string value) => ApplyProjectFilter();

    partial void OnSelectedProjectEntryChanged(ProjectListEntry? value)
    {
        if (value is null || (CurrentProjectPath is not null && IsSamePath(value.Path, CurrentProjectPath))) return;
        SelectProject(value.Path);
    }

    partial void OnSelectedProjectTypeChanged(ProjectType value) => UpdateToolAvailability();

    partial void OnActiveProfileNameChanged(string? value)
    {
        if (_isUpdatingProfileList || value is null || CurrentProjectName is not { } projectName) return;
        var profile = _profileService.GetProfile(projectName, value);
        if (profile is not null) ActivateProfile(profile);
    }

    partial void OnIsLaunchInProgressChanged(bool value) => RefreshCommandStates();

    partial void OnIsStopInProgressChanged(bool value) => RefreshCommandStates();

    // ════════════════════════════════════════════════════════════
    //  PROJECTS
    // ════════════════════════════════════════════════════════════

    [RelayCommand]
    private void RefreshProjects()
    {
        var projectPaths = _projectScanner.GetProjects();
        _projectEntries = BuildProjectEntries(projectPaths);
        if (CurrentProjectPath is not null && !_projectEntries.Any(entry => IsSamePath(entry.Path, CurrentProjectPath)))
            ClearProjectSelection();
        ApplyProjectFilter();
        if (Directory.Exists(AppSettings.HtdocsPath))
            _launchLog.Info($"📁 {projectPaths.Count} projet(s) trouvé(s) dans {AppSettings.HtdocsPath}");
        else
            _launchLog.Error($"❌ Dossier des projets introuvable : {AppSettings.HtdocsPath}");
    }

    /// <summary>
    /// Builds the list entries : recently launched projects first, then the others alphabetically.
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

    /// <summary>Filters the projects list by the search text and keeps the current project selected.</summary>
    private void ApplyProjectFilter()
    {
        VisibleProjects.Clear();
        foreach (var projectEntry in _projectEntries.Where(entry => string.IsNullOrWhiteSpace(SearchText) || entry.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)))
            VisibleProjects.Add(projectEntry);
        SelectedProjectEntry = VisibleProjects.FirstOrDefault(entry => CurrentProjectPath is not null && IsSamePath(entry.Path, CurrentProjectPath));
    }

    private void SelectProject(string projectPath)
    {
        var projectName = Path.GetFileName(projectPath);
        var projectDetection = _projectScanner.DetectProject(projectPath);
        CurrentProjectPath = projectPath;
        IsSymfonyDetected = projectDetection.IsSymfony;
        if (projectDetection.IsSymfony) _launchLog.Info($"✅ Symfony détecté automatiquement dans « {projectName} »");
        LoadProfilesForProject(projectName, projectDetection);
        StatusText = $"Prêt à lancer : {projectName}";
    }

    private void ClearProjectSelection()
    {
        CurrentProjectPath = null;
        IsSymfonyDetected = false;
        StatusText = NoProjectStatus;
        UpdateProfileList(Array.Empty<string>(), null);
    }

    private void RegisterRecentProject(string projectPath)
    {
        _recentProjectsService.RegisterLaunch(projectPath);
        _projectEntries = BuildProjectEntries(_projectScanner.GetProjects());
        ApplyProjectFilter();
    }

    private static bool IsSamePath(string firstPath, string secondPath)
        => string.Equals(Path.TrimEndingDirectorySeparator(firstPath), Path.TrimEndingDirectorySeparator(secondPath), StringComparison.OrdinalIgnoreCase);

    // ════════════════════════════════════════════════════════════
    //  OPTIONS PANEL
    // ════════════════════════════════════════════════════════════

    private void UpdateToolAvailability()
    {
        foreach (var toolCategoryViewModel in ToolCategoryViewModels) toolCategoryViewModel.UpdateAvailability(SelectedProjectType);
    }

    /// <summary>Displays a profile in the options panel.</summary>
    private void ApplyProfile(ProjectProfile profile)
    {
        SelectedProjectType = profile.ProjectType;
        foreach (var toolCategoryViewModel in ToolCategoryViewModels) toolCategoryViewModel.ApplyProfile(profile);
    }

    /// <summary>Captures the options panel as a profile. This profile is also the launch request.</summary>
    private ProjectProfile CaptureCurrentOptions(string profileName)
    {
        var capturedProfile = new ProjectProfile { Name = profileName, ProjectType = SelectedProjectType };
        foreach (var toolCategoryViewModel in ToolCategoryViewModels) toolCategoryViewModel.CaptureInto(capturedProfile);
        return capturedProfile;
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

    /// <summary>Fills the profiles dropdown and selects a profile, without loading it.</summary>
    private void UpdateProfileList(IEnumerable<string> profileNames, string? selectedProfileName)
    {
        var newProfileNames = profileNames.ToList();
        _isUpdatingProfileList = true;
        try
        {
            ProfileNames.Clear();
            foreach (var profileName in newProfileNames) ProfileNames.Add(profileName);
            ActiveProfileName = selectedProfileName;
        }
        finally
        {
            _isUpdatingProfileList = false;
        }
        OnPropertyChanged(nameof(HasSeveralProfiles));
        OnPropertyChanged(nameof(LaunchButtonLabel));
        RefreshCommandStates();
    }

    /// <summary>Displays a profile and remembers it as the last used one of the project.</summary>
    private void ActivateProfile(ProjectProfile profile)
    {
        ApplyProfile(profile);
        RememberActiveProfile(profile.Name);
        _launchLog.Info($"📂 Profil « {profile.Name} » chargé");
    }

    private void RememberActiveProfile(string profileName)
    {
        if (CurrentProjectName is { } projectName) _profileService.SaveLastUsedProfile(projectName, profileName);
    }

    private bool CanEditProfiles() => HasProject && ActiveProfileName is not null;

    private bool CanDeleteProfile() => CanEditProfiles() && ProfileNames.Count > 1;

    [RelayCommand(CanExecute = nameof(CanEditProfiles))]
    private void SaveProfile()
    {
        if (CurrentProjectName is not { } projectName || ActiveProfileName is not { } profileName) return;
        _profileService.SaveProfile(projectName, CaptureCurrentOptions(profileName));
        _launchLog.Info($"💾 Profil « {profileName} » sauvegardé");
        StatusText = $"✅ Profil « {profileName} » sauvegardé";
    }

    [RelayCommand(CanExecute = nameof(CanEditProfiles))]
    private void CreateProfile()
    {
        if (CurrentProjectName is not { } projectName) return;
        var newProfileName = AskUniqueProfileName("Nouveau profil", "Créer", string.Empty);
        if (newProfileName is null) return;
        _profileService.SaveProfile(projectName, CaptureCurrentOptions(newProfileName));
        UpdateProfileList(ProfileNames.Append(newProfileName), newProfileName);
        RememberActiveProfile(newProfileName);
        _launchLog.Info($"✨ Nouveau profil « {newProfileName} » créé");
    }

    [RelayCommand(CanExecute = nameof(CanEditProfiles))]
    private void RenameProfile()
    {
        if (CurrentProjectName is not { } projectName || ActiveProfileName is not { } currentProfileName) return;
        var newProfileName = AskUniqueProfileName("Renommer le profil", "Renommer", currentProfileName);
        if (newProfileName is null || newProfileName == currentProfileName) return;
        _profileService.RenameProfile(projectName, currentProfileName, newProfileName);
        UpdateProfileList(ProfileNames.Select(profileName => profileName == currentProfileName ? newProfileName : profileName), newProfileName);
        _launchLog.Info($"✏️ Profil « {currentProfileName} » renommé en « {newProfileName} »");
    }

    [RelayCommand(CanExecute = nameof(CanDeleteProfile))]
    private void DeleteProfile()
    {
        if (CurrentProjectPath is not { } projectPath || CurrentProjectName is not { } projectName || ActiveProfileName is not { } profileNameToDelete) return;
        if (!_userInteractionService.Confirm($"Supprimer le profil « {profileNameToDelete} » ?", "Confirmation")) return;
        _profileService.DeleteProfile(projectName, profileNameToDelete);
        _launchLog.Info($"🗑️ Profil « {profileNameToDelete} » supprimé");
        LoadProfilesForProject(projectName, _projectScanner.DetectProject(projectPath));
    }

    /// <summary>Asks for a profile name until it is unique. Returns null when cancelled.</summary>
    private string? AskUniqueProfileName(string dialogTitle, string confirmLabel, string initialProfileName)
    {
        var proposedProfileName = initialProfileName;
        while (true)
        {
            var profileName = _userInteractionService.AskProfileName(dialogTitle, confirmLabel, proposedProfileName);
            if (profileName is null) return null;
            if (profileName == initialProfileName || !ProfileNames.Contains(profileName)) return profileName;
            _userInteractionService.ShowWarning($"Un profil « {profileName} » existe déjà.", "Nom existant");
            proposedProfileName = profileName;
        }
    }

    // ════════════════════════════════════════════════════════════
    //  LAUNCH / STOP
    // ════════════════════════════════════════════════════════════

    private bool CanLaunch() => HasProject && !IsLaunchInProgress;

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private async Task LaunchAsync()
    {
        if (CurrentProjectPath is not { } projectPath) return;
        var projectName = Path.GetFileName(projectPath);
        IsLaunchInProgress = true;
        IsLaunchMenuOpen = false;
        StatusText = "⏳ Lancement en cours…";
        _launchLog.Info("═══════════════════════════════");
        _launchLog.Info($"🚀 Lancement de « {projectName} »");
        RegisterRecentProject(projectPath);
        try
        {
            await _launchService.LaunchAsync(projectPath, CaptureCurrentOptions(ActiveProfileName ?? ProjectProfile.DefaultProfileName));
            StatusText = $"✅ Environnement lancé — {projectName}";
            _launchLog.Info("✅ Lancement terminé");
        }
        catch (Exception exception)
        {
            StatusText = "❌ Erreur lors du lancement";
            _launchLog.Error($"❌ Erreur : {exception.Message}");
        }
        finally
        {
            IsLaunchInProgress = false;
            _serviceMonitor.RefreshStatus();
        }
    }

    /// <summary>Selects a profile from the launch menu, then launches it.</summary>
    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private async Task LaunchProfileAsync(string profileName)
    {
        IsLaunchMenuOpen = false;
        ActiveProfileName = profileName;
        await LaunchAsync();
    }

    private bool CanStopAll() => !IsStopInProgress;

    [RelayCommand(CanExecute = nameof(CanStopAll))]
    private async Task StopAllAsync()
    {
        if (!_userInteractionService.Confirm("Arrêter les services et fermer les éditeurs lancés par DevLauncher ?", "⏹ Tout arrêter")) return;
        IsStopInProgress = true;
        _launchLog.Info("⏹ Arrêt de l'environnement…");
        try
        {
            await _launchService.StopAllAsync();
            StatusText = "⏹ Environnement arrêté";
        }
        catch (Exception exception)
        {
            _launchLog.Error($"❌ Erreur lors de l'arrêt : {exception.Message}");
        }
        finally
        {
            IsStopInProgress = false;
            _serviceMonitor.RefreshStatus();
        }
    }

    private void RefreshCommandStates()
    {
        LaunchCommand.NotifyCanExecuteChanged();
        LaunchProfileCommand.NotifyCanExecuteChanged();
        StopAllCommand.NotifyCanExecuteChanged();
        SaveProfileCommand.NotifyCanExecuteChanged();
        CreateProfileCommand.NotifyCanExecuteChanged();
        RenameProfileCommand.NotifyCanExecuteChanged();
        DeleteProfileCommand.NotifyCanExecuteChanged();
    }

    // ════════════════════════════════════════════════════════════
    //  SETTINGS
    // ════════════════════════════════════════════════════════════

    [RelayCommand]
    private void OpenSettings()
    {
        if (!_userInteractionService.EditSettings()) return;
        _launchLog.Info("⚙️ Paramètres mis à jour");
        foreach (var toolCategoryViewModel in ToolCategoryViewModels) toolCategoryViewModel.ReloadOptionChoices();
        RefreshProjects();
    }

    // ════════════════════════════════════════════════════════════
    //  LOG AND SERVICE STATUS
    // ════════════════════════════════════════════════════════════

    [RelayCommand]
    private void ClearLog() => LogEntries.Clear();

    [RelayCommand]
    private void CopyLog() => _userInteractionService.CopyToClipboard(string.Join(Environment.NewLine, LogEntries.Select(logEntry => logEntry.Text)));

    private void OnMessageLogged(string message, bool isError) => RunOnUiThread(() =>
    {
        LogEntries.Add(new LogEntry($"[{DateTime.Now:HH:mm:ss}] {message}", isError));
        while (LogEntries.Count > MaximumLogEntryCount) LogEntries.RemoveAt(0);
    });

    private void OnServiceStatusChanged(string serviceName, bool isRunning) => RunOnUiThread(() =>
    {
        var serviceIndicator = ServiceIndicators.FirstOrDefault(indicator => indicator.ServiceName == serviceName);
        if (serviceIndicator is not null) serviceIndicator.IsRunning = isRunning;
    });

    /// <summary>Runs the action on the UI thread : immediately when already on it, queued otherwise.</summary>
    private void RunOnUiThread(Action action)
    {
        if (_uiDispatcher.CheckAccess()) action();
        else _uiDispatcher.BeginInvoke(action);
    }

    public void Dispose()
    {
        _launchService.RestoreProjectFiles();
        _launchLog.MessageLogged -= OnMessageLogged;
        _serviceMonitor.StatusChanged -= OnServiceStatusChanged;
        _serviceMonitor.Dispose();
        _processEventWatcher.Dispose();
    }
}
