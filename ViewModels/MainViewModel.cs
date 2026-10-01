using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevLauncher.Models;
using DevLauncher.Services;
using DevLauncher.Services.Hosting;
using DevLauncher.Services.Tools;

namespace DevLauncher.ViewModels;

/// <summary>
/// State and actions of the main window : projects lists, options panel built from the tool catalog,
/// profiles, launch and stop, service indicators, launch log and service logs.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private const string DefaultLaunchButtonLabel = "▶ Lancer l'environnement";
    private const string NoProjectStatus = "Sélectionne un projet pour commencer";

    private readonly ProjectScanner _projectScanner;
    private readonly ProfileService _profileService;
    private readonly RecentProjectsService _recentProjectsService;
    private readonly LaunchService _launchService;
    private readonly ServiceProcessHost _serviceProcessHost;
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
        ServiceProcessHost serviceProcessHost,
        ServiceMonitor serviceMonitor,
        ProcessEventWatcher processEventWatcher,
        LaunchLog launchLog,
        IUserInteractionService userInteractionService)
    {
        _projectScanner = projectScanner;
        _profileService = profileService;
        _recentProjectsService = recentProjectsService;
        _launchService = launchService;
        _serviceProcessHost = serviceProcessHost;
        _serviceMonitor = serviceMonitor;
        _processEventWatcher = processEventWatcher;
        _launchLog = launchLog;
        _userInteractionService = userInteractionService;
        _uiDispatcher = Dispatcher.CurrentDispatcher;
        LaunchLogTab = new LogTabViewModel("📋 Lancement");
        LogTabs.Add(LaunchLogTab);
        _selectedLogTab = LaunchLogTab;
        foreach (var toolCategory in ToolCategories.All)
            ToolCategoryViewModels.Add(new ToolCategoryViewModel(toolCategory, toolCatalog.GetCategoryTools(toolCategory)));
        foreach (var serviceName in new[] { "Apache", "MySQL", "FileZilla" })
            ServiceIndicators.Add(new ServiceIndicatorViewModel(serviceName));
        _launchLog.MessageLogged += OnMessageLogged;
        _serviceProcessHost.ServiceCreated += OnServiceCreated;
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

    public ObservableCollection<ProjectListEntry> RecentProjects { get; } = new();
    public ObservableCollection<ProjectListEntry> VisibleProjects { get; } = new();
    public ObservableCollection<ToolCategoryViewModel> ToolCategoryViewModels { get; } = new();
    public ObservableCollection<string> ProfileNames { get; } = new();
    public ObservableCollection<ServiceIndicatorViewModel> ServiceIndicators { get; } = new();
    public ObservableCollection<LogTabViewModel> LogTabs { get; } = new();
    public LogTabViewModel LaunchLogTab { get; }

    public IReadOnlyList<ProjectTypeOption> ProjectTypeOptions { get; } = ProjectTypeLabels.All
        .Select(projectTypeLabel => new ProjectTypeOption(projectTypeLabel.ProjectType, projectTypeLabel.Label))
        .ToList();

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Item selected in the recent projects list. Kept in sync with the current project.</summary>
    [ObservableProperty]
    private ProjectListEntry? _selectedRecentProject;

    /// <summary>Item selected in the projects list. Becomes null when the current project is filtered out, without deselecting it.</summary>
    [ObservableProperty]
    private ProjectListEntry? _selectedProjectEntry;

    [ObservableProperty]
    private LogTabViewModel? _selectedLogTab;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProject), nameof(CurrentProjectDisplayPath), nameof(HasSeveralProfiles), nameof(LaunchButtonLabel))]
    private string? _currentProjectPath;

    /// <summary>Framework detected in the selected project, null when none is recognized.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetectedProjectType))]
    private string? _detectedProjectTypeText;

    public bool HasDetectedProjectType => DetectedProjectTypeText is not null;

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
    public bool HasActiveEnvironment => _launchService.HasActiveEnvironment;
    public bool HasRecentProjects => RecentProjects.Count > 0;
    public bool IsIdle => !IsLaunchInProgress;
    public bool HasSeveralProfiles => HasProject && ProfileNames.Count > 1;
    public string CurrentProjectDisplayPath => CurrentProjectPath ?? "Aucun projet sélectionné";
    public string LaunchButtonLabel => HasSeveralProfiles ? $"▶ {ActiveProfileName}" : DefaultLaunchButtonLabel;
    private string? CurrentProjectName => CurrentProjectPath is null ? null : Path.GetFileName(CurrentProjectPath);

    partial void OnSearchTextChanged(string value) => ApplyProjectFilter();

    partial void OnSelectedRecentProjectChanged(ProjectListEntry? value) => OnProjectEntryPicked(value);

    partial void OnSelectedProjectEntryChanged(ProjectListEntry? value) => OnProjectEntryPicked(value);

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
        foreach (var missingFolder in AppSettings.ProjectRoots.Concat(AppSettings.ExtraProjectPaths).Where(folder => !Directory.Exists(folder)))
            _launchLog.Error($"❌ Dossier introuvable : {missingFolder}");
        _launchLog.Info($"📁 {projectPaths.Count} projet(s) trouvé(s) dans {AppSettings.ProjectRoots.Count} dossier(s) et {AppSettings.ExtraProjectPaths.Count} projet(s) ajouté(s)");
    }

    /// <summary>Adds a project folder located anywhere, saves it in the settings and selects it.</summary>
    [RelayCommand]
    private void AddProject()
    {
        var projectPath = _userInteractionService.PickFolder("Choisis le dossier du projet à ajouter");
        if (projectPath is null) return;
        if (!_projectEntries.Any(entry => IsSamePath(entry.Path, projectPath)))
        {
            AppSettings.ExtraProjectPaths.Add(projectPath);
            SettingsService.Save();
            _launchLog.Info($"➕ Projet ajouté : {projectPath}");
            RefreshProjects();
        }
        SelectProject(_projectEntries.FirstOrDefault(entry => IsSamePath(entry.Path, projectPath))?.Path ?? projectPath);
        SynchronizeListSelections();
    }

    /// <summary>
    /// Builds the list entries. The recent projects only change after a launch, never during a click,
    /// so that the lists do not move under the cursor.
    /// </summary>
    private static List<ProjectListEntry> BuildProjectEntries(IReadOnlyCollection<string> projectPaths)
    {
        var duplicatedNames = projectPaths
            .GroupBy(projectPath => Path.GetFileName(projectPath), StringComparer.OrdinalIgnoreCase)
            .Where(nameGroup => nameGroup.Count() > 1)
            .Select(nameGroup => nameGroup.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return projectPaths.Select(projectPath => CreateProjectEntry(projectPath, duplicatedNames.Contains(Path.GetFileName(projectPath)))).ToList();
    }

    private List<ProjectListEntry> BuildRecentProjectEntries()
        => _recentProjectsService.GetRecentProjectPaths()
            .Where(Directory.Exists)
            .Select(projectPath => CreateProjectEntry(projectPath, isNameDuplicated: false))
            .ToList();

    /// <summary>Projects with the same name in different folders are told apart by their parent folder.</summary>
    private static ProjectListEntry CreateProjectEntry(string projectPath, bool isNameDuplicated)
    {
        var projectName = Path.GetFileName(projectPath);
        var parentFolderName = Path.GetFileName(Path.GetDirectoryName(projectPath));
        return new ProjectListEntry(isNameDuplicated ? $"{projectName} ({parentFolderName})" : projectName, projectPath);
    }

    /// <summary>Filters both lists by the search text and keeps the current project selected in each of them.</summary>
    private void ApplyProjectFilter()
    {
        bool MatchesSearch(ProjectListEntry entry) => string.IsNullOrWhiteSpace(SearchText) || entry.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
        RecentProjects.Clear();
        foreach (var recentProjectEntry in BuildRecentProjectEntries().Where(MatchesSearch)) RecentProjects.Add(recentProjectEntry);
        VisibleProjects.Clear();
        foreach (var projectEntry in _projectEntries.Where(MatchesSearch)) VisibleProjects.Add(projectEntry);
        OnPropertyChanged(nameof(HasRecentProjects));
        SynchronizeListSelections();
    }

    private void SynchronizeListSelections()
    {
        SelectedRecentProject = RecentProjects.FirstOrDefault(entry => CurrentProjectPath is not null && IsSamePath(entry.Path, CurrentProjectPath));
        SelectedProjectEntry = VisibleProjects.FirstOrDefault(entry => CurrentProjectPath is not null && IsSamePath(entry.Path, CurrentProjectPath));
    }

    private void OnProjectEntryPicked(ProjectListEntry? projectEntry)
    {
        if (projectEntry is null || (CurrentProjectPath is not null && IsSamePath(projectEntry.Path, CurrentProjectPath))) return;
        SelectProject(projectEntry.Path);
        SynchronizeListSelections();
    }

    private void SelectProject(string projectPath)
    {
        var projectName = Path.GetFileName(projectPath);
        var projectDetection = _projectScanner.DetectProject(projectPath);
        CurrentProjectPath = projectPath;
        DetectedProjectTypeText = projectDetection.ProjectType == ProjectType.Other
            ? null
            : $"✅ {ProjectTypeLabels.GetName(projectDetection.ProjectType)} détecté automatiquement";
        if (DetectedProjectTypeText is not null) _launchLog.Info($"{DetectedProjectTypeText} dans « {projectName} »");
        // The choices can depend on the project (sessions of an assistant) : they are read before the profile is applied.
        ReloadOptionChoices();
        LoadProfilesForProject(projectName, projectDetection);
        StatusText = $"Prêt à lancer : {projectName}";
    }

    private void ClearProjectSelection()
    {
        CurrentProjectPath = null;
        DetectedProjectTypeText = null;
        StatusText = NoProjectStatus;
        UpdateProfileList(Array.Empty<string>(), null);
    }

    private void RegisterRecentProject(string projectPath)
    {
        _recentProjectsService.RegisterLaunch(projectPath);
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

    private void ReloadOptionChoices()
    {
        var optionContext = new ToolOptionContext(CurrentProjectPath);
        foreach (var toolCategoryViewModel in ToolCategoryViewModels) toolCategoryViewModel.ReloadOptionChoices(optionContext);
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
        await StopEnvironmentAsync();
    }

    /// <summary>
    /// Asks what to do with the running environment before the application closes.
    /// Returns false when the user cancels the closing.
    /// </summary>
    public async Task<bool> PrepareExitAsync()
    {
        if (!HasActiveEnvironment) return true;
        var shouldStopEnvironment = _userInteractionService.AskYesNoCancel(
            "Arrêter l'environnement avant de quitter (services, XAMPP, éditeurs) ?\n\n" +
            "Non : XAMPP et les éditeurs restent ouverts, les services lancés par DevLauncher sont arrêtés.",
            "Quitter DevLauncher");
        if (shouldStopEnvironment is null) return false;
        if (shouldStopEnvironment == true) await StopEnvironmentAsync();
        return true;
    }

    private async Task StopEnvironmentAsync()
    {
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
        ReloadOptionChoices();
        UpdateToolAvailability();
        RefreshProjects();
    }

    // ════════════════════════════════════════════════════════════
    //  LOG AND SERVICE STATUS
    // ════════════════════════════════════════════════════════════

    [RelayCommand]
    private void ClearLog() => (SelectedLogTab ?? LaunchLogTab).ClearLines();

    [RelayCommand]
    private void CopyLog() => _userInteractionService.CopyToClipboard((SelectedLogTab ?? LaunchLogTab).GetText());

    private void OnMessageLogged(string message, bool isError) => RunOnUiThread(() => LaunchLogTab.AppendLine(message, isError));

    /// <summary>Adds a log tab for each new service run by DevLauncher.</summary>
    private void OnServiceCreated(HostedService hostedService) => RunOnUiThread(() =>
        LogTabs.Add(new ServiceLogTabViewModel(hostedService, _launchLog, RunOnUiThread, CloseServiceLogTab)));

    private void CloseServiceLogTab(ServiceLogTabViewModel serviceLogTab)
    {
        if (SelectedLogTab == serviceLogTab) SelectedLogTab = LaunchLogTab;
        LogTabs.Remove(serviceLogTab);
        _serviceProcessHost.Remove(serviceLogTab.HostedService);
    }

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
        _launchService.Shutdown();
        _launchLog.MessageLogged -= OnMessageLogged;
        _serviceProcessHost.ServiceCreated -= OnServiceCreated;
        _serviceMonitor.StatusChanged -= OnServiceStatusChanged;
        _serviceMonitor.Dispose();
        _processEventWatcher.Dispose();
    }
}
