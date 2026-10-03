using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevLauncher.Models;
using DevLauncher.Services;
using DevLauncher.Services.Hosting;
using DevLauncher.Services.Mcp;
using DevLauncher.Services.Stacks;
using DevLauncher.Services.Startup;
using DevLauncher.Services.Tools;

namespace DevLauncher.ViewModels;

/// <summary>
/// State and actions of the main window : projects lists, options panel built from the tool catalog,
/// profiles, launch and stop, service indicators, launch log and service logs.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable, ILaunchObserver, IDevLauncherAutomation
{
    private const string DefaultLaunchButtonLabel = "▶ Lancer l'environnement";
    private const string NoProjectStatus = "Sélectionne un projet pour commencer";

    private readonly ProjectScanner _projectScanner;
    private readonly ProfileService _profileService;
    private readonly RecentProjectsService _recentProjectsService;
    private readonly FavoriteProjectsService _favoriteProjectsService;
    private readonly GitStatusService _gitStatusService;
    private readonly LaunchStatisticsService _launchStatisticsService;
    private readonly VirtualHostService _virtualHostService;
    private readonly ProcessLauncher _processLauncher;
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
        FavoriteProjectsService favoriteProjectsService,
        GitStatusService gitStatusService,
        LaunchStatisticsService launchStatisticsService,
        VirtualHostService virtualHostService,
        ProcessLauncher processLauncher,
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
        _favoriteProjectsService = favoriteProjectsService;
        _gitStatusService = gitStatusService;
        _launchStatisticsService = launchStatisticsService;
        _virtualHostService = virtualHostService;
        _processLauncher = processLauncher;
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
        UpdateJumpList();
    }

    // ════════════════════════════════════════════════════════════
    //  BOUND STATE
    // ════════════════════════════════════════════════════════════

    public ObservableCollection<ProjectListEntry> FavoriteProjects { get; } = new();
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

    /// <summary>Item selected in the favorite projects list. Kept in sync with the current project.</summary>
    [ObservableProperty]
    private ProjectListEntry? _selectedFavoriteProject;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteToggleLabel), nameof(FavoriteToggleToolTip))]
    private bool _isCurrentProjectFavorite;

    public string FavoriteToggleLabel => IsCurrentProjectFavorite ? "★" : "☆";

    public string FavoriteToggleToolTip => IsCurrentProjectFavorite ? "Retirer des favoris" : "Épingler dans les favoris";

    /// <summary>Git state of the current project, null when it is not a git repository or not read yet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGitStatus))]
    private string? _gitStatusText;

    public bool HasGitStatus => GitStatusText is not null;

    [ObservableProperty]
    private bool _isGitFetchInProgress;

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

    /// <summary>The profiles of the current project are read from and written to its .devlauncher.json file.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProfileSharingLabel), nameof(ProfileSharingToolTip))]
    private bool _isProfileSharedInProject;

    public string ProfileSharingLabel => IsProfileSharedInProject ? "📌" : "📤";

    public string ProfileSharingToolTip => IsProfileSharedInProject
        ? $"Profils partagés avec le projet ({ProfileService.ProjectProfilesFileName}) — cliquer pour les garder dans DevLauncher uniquement"
        : $"Partager les profils avec le projet ({ProfileService.ProjectProfilesFileName}, versionnable avec git)";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isLaunchInProgress;

    [ObservableProperty]
    private bool _isStopInProgress;

    /// <summary>Advancement of the launch in progress, from 0 to 100.</summary>
    [ObservableProperty]
    private double _launchProgressValue;

    [ObservableProperty]
    private string _launchStepText = string.Empty;

    [ObservableProperty]
    private bool _isLaunchMenuOpen;

    public bool HasProject => CurrentProjectPath is not null;
    public bool HasActiveEnvironment => _launchService.HasActiveEnvironment;
    public bool HasRecentProjects => RecentProjects.Count > 0;
    public bool HasFavoriteProjects => FavoriteProjects.Count > 0;
    public bool IsIdle => !IsLaunchInProgress;
    public bool HasSeveralProfiles => HasProject && ProfileNames.Count > 1;
    public string CurrentProjectDisplayPath => CurrentProjectPath ?? "Aucun projet sélectionné";
    public string LaunchButtonLabel => HasSeveralProfiles ? $"▶ {ActiveProfileName}" : DefaultLaunchButtonLabel;
    private string? CurrentProjectName => CurrentProjectPath is null ? null : Path.GetFileName(CurrentProjectPath);

    partial void OnSearchTextChanged(string value) => ApplyProjectFilter();

    partial void OnSelectedFavoriteProjectChanged(ProjectListEntry? value) => OnProjectEntryPicked(value);

    partial void OnSelectedRecentProjectChanged(ProjectListEntry? value) => OnProjectEntryPicked(value);

    partial void OnSelectedProjectEntryChanged(ProjectListEntry? value) => OnProjectEntryPicked(value);

    partial void OnSelectedProjectTypeChanged(ProjectType value) => UpdateToolAvailability();

    partial void OnActiveProfileNameChanged(string? value)
    {
        if (_isUpdatingProfileList || value is null || CurrentProjectPath is not { } projectPath) return;
        TryRunProfileOperation("Chargement du profil", () =>
        {
            var profile = _profileService.GetProfile(projectPath, value);
            if (profile is not null) ActivateProfile(profile);
        });
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
        if (CurrentProjectPath is not null && !_projectEntries.Any(entry => PathComparer.AreSame(entry.Path, CurrentProjectPath)))
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
        SelectProject(EnsureProjectListed(projectPath));
        SynchronizeListSelections();
    }

    /// <summary>Adds a folder outside of the project roots to the projects added one by one. Returns the path as listed.</summary>
    private string EnsureProjectListed(string projectPath)
    {
        var listedEntry = _projectEntries.FirstOrDefault(entry => PathComparer.AreSame(entry.Path, projectPath));
        if (listedEntry is not null) return listedEntry.Path;
        AppSettings.ExtraProjectPaths.Add(projectPath);
        try
        {
            SettingsService.Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _launchLog.Error($"❌ Sauvegarde des paramètres impossible : {exception.Message}");
        }
        _launchLog.Info($"➕ Projet ajouté : {projectPath}");
        RefreshProjects();
        return _projectEntries.FirstOrDefault(entry => PathComparer.AreSame(entry.Path, projectPath))?.Path ?? projectPath;
    }

    // ════════════════════════════════════════════════════════════
    //  STARTUP COMMANDS (command line, devlauncher:// links, Explorer, jump list)
    // ════════════════════════════════════════════════════════════

    /// <summary>
    /// Selects the requested project (folder name or full path, added to the list when it is outside of it),
    /// applies the requested profile, then launches when asked.
    /// </summary>
    /// <returns>Outcome of the request, as shown in the status bar.</returns>
    public async Task<string> HandleStartupCommandAsync(StartupCommand startupCommand)
    {
        if (startupCommand.Project is not { } requestedProject) return "Aucun projet demandé";
        var projectPath = ResolveRequestedProject(requestedProject);
        if (projectPath is null)
        {
            var notFoundMessage = $"❌ Projet « {requestedProject} » introuvable dans les dossiers de projets";
            _launchLog.Error(notFoundMessage);
            return notFoundMessage;
        }
        if (CurrentProjectPath is null || !PathComparer.AreSame(CurrentProjectPath, projectPath)) SelectProject(projectPath);
        SynchronizeListSelections();
        var profileWarning = string.Empty;
        if (startupCommand.ProfileName is { } requestedProfileName)
        {
            var matchingProfileName = ProfileNames.FirstOrDefault(profileName => profileName.Equals(requestedProfileName, StringComparison.OrdinalIgnoreCase));
            if (matchingProfileName is null)
            {
                profileWarning = $"❌ Profil « {requestedProfileName} » introuvable : profil « {ActiveProfileName} » utilisé. ";
                _launchLog.Error(profileWarning.Trim());
            }
            else
            {
                ActiveProfileName = matchingProfileName;
            }
        }
        if (!startupCommand.ShouldLaunch) return $"{profileWarning}Projet « {Path.GetFileName(projectPath)} » sélectionné";
        if (IsLaunchInProgress)
        {
            _launchLog.Error("⏳ Un lancement est déjà en cours : demande ignorée");
            return "⏳ Un lancement est déjà en cours : demande ignorée";
        }
        await LaunchAsync();
        return profileWarning + StatusText;
    }

    /// <summary>An existing folder is used as is (and listed), a name is looked up among the listed projects.</summary>
    private string? ResolveRequestedProject(string requestedProject)
    {
        if (Path.IsPathRooted(requestedProject) && Directory.Exists(requestedProject))
            return EnsureProjectListed(Path.TrimEndingDirectorySeparator(Path.GetFullPath(requestedProject)));
        return _projectEntries.FirstOrDefault(entry => Path.GetFileName(entry.Path).Equals(requestedProject, StringComparison.OrdinalIgnoreCase))?.Path;
    }

    /// <summary>Copies the devlauncher:// link launching the project with the active profile, to paste in a README or a note.</summary>
    [RelayCommand]
    private void CopyLaunchLink(ProjectListEntry? projectEntry)
    {
        if (GetTargetProjectPath(projectEntry) is not { } projectPath) return;
        var isCurrentProject = CurrentProjectPath is not null && PathComparer.AreSame(projectPath, CurrentProjectPath);
        var launchLink = StartupCommand.BuildLaunchLink(Path.GetFileName(projectPath), isCurrentProject && HasSeveralProfiles ? ActiveProfileName : null);
        _userInteractionService.CopyToClipboard(launchLink);
        StatusText = $"🔗 Lien copié : {launchLink}";
    }

    /// <summary>Puts the favorite and recent projects in the jump list of the taskbar icon.</summary>
    private void UpdateJumpList() => _userInteractionService.UpdateJumpList(BuildFavoriteProjectEntries(), BuildRecentProjectEntries());

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

    private List<ProjectListEntry> BuildFavoriteProjectEntries()
        => _favoriteProjectsService.GetFavoriteProjectPaths()
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
        FavoriteProjects.Clear();
        foreach (var favoriteProjectEntry in BuildFavoriteProjectEntries().Where(MatchesSearch)) FavoriteProjects.Add(favoriteProjectEntry);
        RecentProjects.Clear();
        foreach (var recentProjectEntry in BuildRecentProjectEntries().Where(MatchesSearch)) RecentProjects.Add(recentProjectEntry);
        VisibleProjects.Clear();
        foreach (var projectEntry in _projectEntries.Where(MatchesSearch)) VisibleProjects.Add(projectEntry);
        OnPropertyChanged(nameof(HasRecentProjects));
        OnPropertyChanged(nameof(HasFavoriteProjects));
        SynchronizeListSelections();
    }

    private void SynchronizeListSelections()
    {
        SelectedFavoriteProject = FavoriteProjects.FirstOrDefault(entry => CurrentProjectPath is not null && PathComparer.AreSame(entry.Path, CurrentProjectPath));
        SelectedRecentProject = RecentProjects.FirstOrDefault(entry => CurrentProjectPath is not null && PathComparer.AreSame(entry.Path, CurrentProjectPath));
        SelectedProjectEntry = VisibleProjects.FirstOrDefault(entry => CurrentProjectPath is not null && PathComparer.AreSame(entry.Path, CurrentProjectPath));
    }

    private void OnProjectEntryPicked(ProjectListEntry? projectEntry)
    {
        if (projectEntry is null || (CurrentProjectPath is not null && PathComparer.AreSame(projectEntry.Path, CurrentProjectPath))) return;
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
        LoadProfilesForProject(projectPath, projectDetection);
        IsCurrentProjectFavorite = _favoriteProjectsService.IsFavorite(projectPath);
        StatusText = $"Prêt à lancer : {projectName}";
        _ = RefreshGitStatusAsync();
    }

    private void ClearProjectSelection()
    {
        CurrentProjectPath = null;
        DetectedProjectTypeText = null;
        IsProfileSharedInProject = false;
        IsCurrentProjectFavorite = false;
        GitStatusText = null;
        StatusText = NoProjectStatus;
        UpdateProfileList(Array.Empty<string>(), null);
    }

    private void RegisterRecentProject(string projectPath)
    {
        _recentProjectsService.RegisterLaunch(projectPath);
        ApplyProjectFilter();
        UpdateJumpList();
    }


    // ════════════════════════════════════════════════════════════
    //  FAVORITES, GIT AND QUICK ACTIONS
    // ════════════════════════════════════════════════════════════

    /// <summary>Project targeted by a quick action : the item of a context menu, or else the current project.</summary>
    private string? GetTargetProjectPath(ProjectListEntry? projectEntry) => projectEntry?.Path ?? CurrentProjectPath;

    [RelayCommand]
    private void ToggleFavorite(ProjectListEntry? projectEntry)
    {
        if (GetTargetProjectPath(projectEntry) is not { } projectPath) return;
        try
        {
            var isNowFavorite = _favoriteProjectsService.ToggleFavorite(projectPath);
            _launchLog.Info(isNowFavorite ? $"★ « {Path.GetFileName(projectPath)} » épinglé dans les favoris" : $"☆ « {Path.GetFileName(projectPath)} » retiré des favoris");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _launchLog.Error($"❌ Favoris : {exception.Message}");
        }
        if (CurrentProjectPath is not null) IsCurrentProjectFavorite = _favoriteProjectsService.IsFavorite(CurrentProjectPath);
        ApplyProjectFilter();
        UpdateJumpList();
    }

    [RelayCommand]
    private void OpenInExplorer(ProjectListEntry? projectEntry)
    {
        if (GetTargetProjectPath(projectEntry) is { } projectPath) _processLauncher.StartShellProcess("explorer.exe", $"\"{projectPath}\"");
    }

    [RelayCommand]
    private void CopyProjectPath(ProjectListEntry? projectEntry)
    {
        if (GetTargetProjectPath(projectEntry) is not { } projectPath) return;
        _userInteractionService.CopyToClipboard(projectPath);
        StatusText = "📋 Chemin copié";
    }

    /// <summary>Opens the local URL of the project : the one of its configuration, or the one announced by its running server.</summary>
    [RelayCommand]
    private void OpenLocalUrl(ProjectListEntry? projectEntry)
    {
        if (GetTargetProjectPath(projectEntry) is not { } projectPath) return;
        var localUrl = ProjectUrlResolver.GetKnownUrl(projectPath, GetProjectType(projectPath), _serviceProcessHost.TryGetAnnouncedUrl(projectPath), _virtualHostService.FindHostName(projectPath));
        if (localUrl is null)
        {
            _launchLog.Error($"❌ URL locale de « {Path.GetFileName(projectPath)} » inconnue : elle est annoncée par son serveur de développement, à lancer d'abord");
            return;
        }
        _launchLog.Info($"🌍 Ouverture de {localUrl}");
        _processLauncher.StartShellProcess(localUrl);
    }

    /// <summary>Copies a Markdown description of the project (stack, tree, commands, URL, git) to paste into an AI conversation.</summary>
    [RelayCommand]
    private async Task CopyProjectContextAsync(ProjectListEntry? projectEntry)
    {
        if (GetTargetProjectPath(projectEntry) is not { } projectPath) return;
        var projectDetection = _projectScanner.DetectProject(projectPath);
        var projectType = GetProjectType(projectPath);
        var gitStatus = await _gitStatusService.GetStatusAsync(projectPath);
        var projectContext = ProjectContextBuilder.Build(new ProjectContextBuilder.ProjectContextInput(
            projectPath,
            projectType,
            projectDetection.UsesTailwindBundle,
            ProjectScanner.GetNpmScripts(projectPath),
            ProjectUrlResolver.GetKnownUrl(projectPath, projectType, _serviceProcessHost.TryGetAnnouncedUrl(projectPath), _virtualHostService.FindHostName(projectPath)),
            gitStatus));
        _userInteractionService.CopyToClipboard(projectContext);
        StatusText = "🧠 Contexte du projet copié : prêt à coller dans une discussion";
        _launchLog.Info($"🧠 Contexte de « {Path.GetFileName(projectPath)} » copié dans le presse-papiers");
    }

    /// <summary>
    /// Creates the virtual host of the project (projet.test : Apache and hosts file), or removes it when it exists,
    /// then restarts Apache when it runs so that it reads its new configuration.
    /// </summary>
    [RelayCommand]
    private async Task ToggleVirtualHostAsync(ProjectListEntry? projectEntry)
    {
        if (GetTargetProjectPath(projectEntry) is not { } projectPath) return;
        var existingHostName = _virtualHostService.FindHostName(projectPath);
        try
        {
            if (existingHostName is null)
            {
                var hostName = _virtualHostService.Add(projectPath, AppSettings.LocalWebPort);
                _launchLog.Info($"🌐 Hôte virtuel créé : {ProjectUrlResolver.BuildVirtualHostUrl(hostName, AppSettings.LocalWebPort)} → {VirtualHostService.FindDocumentRoot(projectPath)}");
            }
            else
            {
                _virtualHostService.Remove(projectPath, AppSettings.LocalWebPort);
                _launchLog.Info($"🌐 Hôte virtuel {existingHostName} supprimé");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _launchLog.Error($"❌ Hôte virtuel : {exception.Message}");
            return;
        }
        if (!ProcessHelper.IsProcessRunning("httpd"))
        {
            _launchLog.Info("   ℹ️ Pris en compte au prochain démarrage d'Apache");
            return;
        }
        _launchLog.Info("↻ Redémarrage d'Apache pour lire sa configuration…");
        await _processLauncher.StopProcessesAsync("Apache", process => process.ProcessName.Equals("httpd", StringComparison.OrdinalIgnoreCase));
        _processLauncher.StartHiddenProcess(AppSettings.ApacheExe, null);
        _serviceMonitor.RefreshStatus();
    }

    /// <summary>The type chosen in the options panel for the current project, the detected type for another one.</summary>
    private ProjectType GetProjectType(string projectPath)
        => CurrentProjectPath is not null && PathComparer.AreSame(projectPath, CurrentProjectPath)
            ? SelectedProjectType
            : _projectScanner.DetectProject(projectPath).ProjectType;

    [RelayCommand]
    private async Task RefreshGitStatusAsync()
    {
        if (CurrentProjectPath is not { } projectPath) return;
        var gitStatus = await _gitStatusService.GetStatusAsync(projectPath);
        // Another project may have been selected while git was running.
        if (CurrentProjectPath is null || !PathComparer.AreSame(CurrentProjectPath, projectPath)) return;
        GitStatusText = gitStatus?.Summary;
    }

    /// <summary>Downloads the remote state of the repository, then shows how many commits are behind.</summary>
    [RelayCommand]
    private async Task FetchGitAsync()
    {
        if (CurrentProjectPath is not { } projectPath || IsGitFetchInProgress) return;
        IsGitFetchInProgress = true;
        try
        {
            _launchLog.Info($"🌿 git fetch de « {Path.GetFileName(projectPath)} »…");
            if (!await _gitStatusService.FetchAsync(projectPath)) _launchLog.Error("❌ git fetch a échoué (pas de dépôt distant, pas de réseau ou authentification requise)");
            await RefreshGitStatusAsync();
        }
        finally
        {
            IsGitFetchInProgress = false;
        }
    }

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

    /// <summary>
    /// Loads the profiles of a project, creating the default profile when it has none.
    /// When the profiles cannot be read, the default profile is shown without being saved.
    /// </summary>
    private void LoadProfilesForProject(string projectPath, ProjectDetection projectDetection)
    {
        IsProfileSharedInProject = ProfileService.IsSharedInProject(projectPath);
        List<ProjectProfile> profiles;
        try
        {
            profiles = _profileService.GetProfiles(projectPath);
            if (profiles.Count == 0)
            {
                var defaultProfile = ProjectProfile.CreateDefault(projectDetection);
                _profileService.SaveProfile(projectPath, defaultProfile);
                profiles.Add(defaultProfile);
            }
        }
        catch (Exception exception) when (IsProfileStorageException(exception))
        {
            _launchLog.Error($"❌ Profils de « {Path.GetFileName(projectPath)} » illisibles : {exception.Message} — profil par défaut affiché, non sauvegardé");
            profiles = new List<ProjectProfile> { ProjectProfile.CreateDefault(projectDetection) };
        }
        var lastUsedProfileName = _profileService.GetLastUsedProfile(projectPath);
        var profileToActivate = profiles.FirstOrDefault(profile => profile.Name == lastUsedProfileName) ?? profiles[0];
        UpdateProfileList(profiles.Select(profile => profile.Name), profileToActivate.Name);
        ActivateProfile(profileToActivate);
    }

    private static bool IsProfileStorageException(Exception exception)
        => exception is IOException or UnauthorizedAccessException or InvalidDataException;

    /// <summary>Runs a profile read or write, logging the storage errors instead of letting them escape.</summary>
    private bool TryRunProfileOperation(string operationName, Action profileOperation)
    {
        try
        {
            profileOperation();
            return true;
        }
        catch (Exception exception) when (IsProfileStorageException(exception))
        {
            _launchLog.Error($"❌ {operationName} impossible : {exception.Message}");
            StatusText = $"❌ {operationName} impossible";
            return false;
        }
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
        if (CurrentProjectPath is { } projectPath) TryRunProfileOperation("Mémorisation du profil", () => _profileService.SaveLastUsedProfile(projectPath, profileName));
    }

    private bool CanEditProfiles() => HasProject && ActiveProfileName is not null;

    private bool CanDeleteProfile() => CanEditProfiles() && ProfileNames.Count > 1;

    [RelayCommand(CanExecute = nameof(CanEditProfiles))]
    private void SaveProfile()
    {
        if (CurrentProjectPath is not { } projectPath || ActiveProfileName is not { } profileName) return;
        if (!TryRunProfileOperation("Sauvegarde du profil", () => _profileService.SaveProfile(projectPath, CaptureCurrentOptions(profileName)))) return;
        _launchLog.Info($"💾 Profil « {profileName} » sauvegardé");
        StatusText = $"✅ Profil « {profileName} » sauvegardé";
    }

    [RelayCommand(CanExecute = nameof(CanEditProfiles))]
    private void CreateProfile()
    {
        if (CurrentProjectPath is not { } projectPath) return;
        var newProfileName = AskUniqueProfileName("Nouveau profil", "Créer", string.Empty);
        if (newProfileName is null) return;
        if (!TryRunProfileOperation("Création du profil", () => _profileService.SaveProfile(projectPath, CaptureCurrentOptions(newProfileName)))) return;
        UpdateProfileList(ProfileNames.Append(newProfileName), newProfileName);
        RememberActiveProfile(newProfileName);
        _launchLog.Info($"✨ Nouveau profil « {newProfileName} » créé");
    }

    [RelayCommand(CanExecute = nameof(CanEditProfiles))]
    private void RenameProfile()
    {
        if (CurrentProjectPath is not { } projectPath || ActiveProfileName is not { } currentProfileName) return;
        var newProfileName = AskUniqueProfileName("Renommer le profil", "Renommer", currentProfileName);
        if (newProfileName is null || newProfileName == currentProfileName) return;
        if (!TryRunProfileOperation("Renommage du profil", () => _profileService.RenameProfile(projectPath, currentProfileName, newProfileName))) return;
        UpdateProfileList(ProfileNames.Select(profileName => profileName == currentProfileName ? newProfileName : profileName), newProfileName);
        _launchLog.Info($"✏️ Profil « {currentProfileName} » renommé en « {newProfileName} »");
    }

    [RelayCommand(CanExecute = nameof(CanDeleteProfile))]
    private void DeleteProfile()
    {
        if (CurrentProjectPath is not { } projectPath || ActiveProfileName is not { } profileNameToDelete) return;
        if (!_userInteractionService.Confirm($"Supprimer le profil « {profileNameToDelete} » ?", "Confirmation")) return;
        if (!TryRunProfileOperation("Suppression du profil", () => _profileService.DeleteProfile(projectPath, profileNameToDelete))) return;
        _launchLog.Info($"🗑️ Profil « {profileNameToDelete} » supprimé");
        LoadProfilesForProject(projectPath, _projectScanner.DetectProject(projectPath));
    }

    /// <summary>Moves the profiles of the project between the local storage and the .devlauncher.json file of the project.</summary>
    [RelayCommand(CanExecute = nameof(CanEditProfiles))]
    private void ToggleProfileSharing()
    {
        if (CurrentProjectPath is not { } projectPath) return;
        if (IsProfileSharedInProject)
        {
            if (!_userInteractionService.Confirm(
                    $"Ne plus partager les profils avec le projet ?\n\nLes profils sont recopiés dans DevLauncher et {ProfileService.ProjectProfilesFileName} est supprimé du dossier du projet.",
                    "Profils du projet")) return;
            if (!TryRunProfileOperation("Arrêt du partage des profils", () => _profileService.StopSharingInProject(projectPath))) return;
            _launchLog.Info($"🔓 Profils de « {CurrentProjectName} » gardés dans DevLauncher, {ProfileService.ProjectProfilesFileName} supprimé");
        }
        else
        {
            if (!_userInteractionService.Confirm(
                    $"Enregistrer les profils dans {ProfileService.ProjectProfilesFileName}, à la racine du projet ?\n\nLe fichier peut être versionné avec git pour partager la configuration avec l'équipe. Le dernier profil utilisé reste personnel.",
                    "Profils du projet")) return;
            if (!TryRunProfileOperation("Partage des profils", () => _profileService.ShareInProject(projectPath))) return;
            _launchLog.Info($"📌 Profils de « {CurrentProjectName} » enregistrés dans {ProfileService.GetProjectProfilesFilePath(projectPath)}");
        }
        LoadProfilesForProject(projectPath, _projectScanner.DetectProject(projectPath));
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
        LaunchProgressValue = 0;
        LaunchStepText = string.Empty;
        StatusText = "⏳ Lancement en cours…";
        _launchLog.Info("═══════════════════════════════");
        _launchLog.Info($"🚀 Lancement de « {projectName} »");
        var launchTime = DateTime.Now;
        var launchStopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (!await _launchService.LaunchAsync(projectPath, CaptureCurrentOptions(ActiveProfileName ?? ProjectProfile.DefaultProfileName), this))
            {
                StatusText = "⏹ Lancement annulé";
                return;
            }
            RegisterRecentProject(projectPath);
            _launchStatisticsService.RecordLaunch(projectPath, launchTime, launchStopwatch.Elapsed);
            StatusText = $"✅ Environnement lancé — {projectName} ({launchStopwatch.Elapsed.TotalSeconds:0.0} s)";
            _launchLog.Info($"✅ Lancement terminé en {launchStopwatch.Elapsed.TotalSeconds:0.0} s");
            _userInteractionService.ShowNotification("✅ Environnement prêt", $"{projectName} lancé en {launchStopwatch.Elapsed.TotalSeconds:0.0} s", isWarning: false, onlyWhenInBackground: true);
        }
        catch (Exception exception)
        {
            StatusText = "❌ Erreur lors du lancement";
            _launchLog.Error($"❌ Erreur : {exception.Message}");
            _userInteractionService.ShowNotification("❌ Lancement en erreur", $"{projectName} : {exception.Message}", isWarning: true, onlyWhenInBackground: true);
        }
        finally
        {
            IsLaunchInProgress = false;
            _serviceMonitor.RefreshStatus();
        }
        await RefreshGitStatusAsync();
    }

    /// <summary>Selects a profile from the launch menu, then launches it.</summary>
    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private async Task LaunchProfileAsync(string profileName)
    {
        IsLaunchMenuOpen = false;
        ActiveProfileName = profileName;
        await LaunchAsync();
    }

    void ILaunchObserver.ReportProgress(LaunchProgress progress) => RunOnUiThread(() =>
    {
        LaunchProgressValue = progress.Percentage;
        LaunchStepText = $"{progress.StepLabel}  ({Math.Min(progress.CompletedStepCount + 1, progress.TotalStepCount)}/{progress.TotalStepCount})";
    });

    PortConflictDecision ILaunchObserver.ResolvePortConflict(PortConflict portConflict) => _userInteractionService.AskPortConflict(portConflict);

    /// <summary>Favorite then recent projects, without duplicates : the quick launch list of the notification area.</summary>
    public IReadOnlyList<ProjectListEntry> GetQuickLaunchProjects()
        => BuildFavoriteProjectEntries()
            .Concat(BuildRecentProjectEntries())
            .DistinctBy(projectEntry => projectEntry.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Opens the command palette and runs the chosen command.</summary>
    [RelayCommand]
    private async Task OpenCommandPaletteAsync()
    {
        var chosenCommand = _userInteractionService.ShowCommandPalette(new CommandPaletteViewModel(BuildPaletteCommands()));
        if (chosenCommand is not null) await chosenCommand.ExecuteAsync();
    }

    /// <summary>Every action reachable from the palette : the actions of the current project, the general actions, then each project.</summary>
    private List<PaletteCommand> BuildPaletteCommands()
    {
        var paletteCommands = new List<PaletteCommand>();
        Task RunCommand(Action action)
        {
            action();
            return Task.CompletedTask;
        }
        if (CurrentProjectName is { } currentProjectName)
        {
            const string currentProjectCategory = "Projet actuel";
            if (CanLaunch()) paletteCommands.Add(new PaletteCommand($"▶ Lancer {currentProjectName}", currentProjectCategory, LaunchAsync));
            foreach (var profileName in ProfileNames.Where(_ => HasSeveralProfiles && CanLaunch()))
                paletteCommands.Add(new PaletteCommand($"▶ Lancer {currentProjectName} avec le profil {profileName}", currentProjectCategory, () => LaunchProfileAsync(profileName)));
            paletteCommands.Add(new PaletteCommand("🌍 Ouvrir l'URL locale", currentProjectCategory, () => RunCommand(() => OpenLocalUrl(null))));
            paletteCommands.Add(new PaletteCommand("🧠 Copier le contexte pour l'IA", currentProjectCategory, () => CopyProjectContextAsync(null)));
            paletteCommands.Add(new PaletteCommand("📂 Ouvrir dans l'Explorateur", currentProjectCategory, () => RunCommand(() => OpenInExplorer(null))));
            paletteCommands.Add(new PaletteCommand("📋 Copier le chemin", currentProjectCategory, () => RunCommand(() => CopyProjectPath(null))));
            paletteCommands.Add(new PaletteCommand("🔗 Copier le lien de lancement", currentProjectCategory, () => RunCommand(() => CopyLaunchLink(null))));
            paletteCommands.Add(new PaletteCommand(IsCurrentProjectFavorite ? "☆ Retirer des favoris" : "★ Épingler dans les favoris", currentProjectCategory, () => RunCommand(() => ToggleFavorite(null))));
            paletteCommands.Add(new PaletteCommand("⟳ git fetch", currentProjectCategory, FetchGitAsync));
            var virtualHostName = CurrentProjectPath is null ? null : _virtualHostService.FindHostName(CurrentProjectPath);
            paletteCommands.Add(new PaletteCommand(virtualHostName is null ? $"🌐 Créer l'hôte virtuel {VirtualHostService.BuildHostName(CurrentProjectPath!)}" : $"🌐 Supprimer l'hôte virtuel {virtualHostName}",
                currentProjectCategory, () => ToggleVirtualHostAsync(null)));
        }
        const string generalCategory = "Général";
        if (CanStopAll()) paletteCommands.Add(new PaletteCommand("⏹ Tout arrêter", generalCategory, StopAllAsync));
        paletteCommands.Add(new PaletteCommand("⚙️ Paramètres", generalCategory, () => RunCommand(OpenSettings)));
        paletteCommands.Add(new PaletteCommand("📊 Statistiques", generalCategory, () => RunCommand(ShowStatistics)));
        paletteCommands.Add(new PaletteCommand("↻ Rafraîchir les projets", generalCategory, () => RunCommand(RefreshProjects)));
        paletteCommands.Add(new PaletteCommand("➕ Ajouter un projet", generalCategory, () => RunCommand(AddProject)));
        var quickLaunchProjects = GetQuickLaunchProjects();
        var orderedProjects = quickLaunchProjects.Concat(_projectEntries).DistinctBy(projectEntry => projectEntry.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var projectEntry in orderedProjects)
        {
            paletteCommands.Add(new PaletteCommand($"🚀 Lancer {projectEntry.Name}", "Projets", () => HandleStartupCommandAsync(new StartupCommand(projectEntry.Path, null, true, false))));
            paletteCommands.Add(new PaletteCommand($"📁 Ouvrir {projectEntry.Name}", "Projets", () => HandleStartupCommandAsync(new StartupCommand(projectEntry.Path, null, false, false))));
        }
        return paletteCommands;
    }

    [RelayCommand]
    private void ShowStatistics() => _userInteractionService.ShowStatistics(_launchStatisticsService.GetStatistics());

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
        ToggleProfileSharingCommand.NotifyCanExecuteChanged();
    }

    // ════════════════════════════════════════════════════════════
    //  SETTINGS
    // ════════════════════════════════════════════════════════════

    [RelayCommand]
    private void OpenSettings()
    {
        var settingsEditResult = _userInteractionService.EditSettings();
        if (settingsEditResult == SettingsEditResult.Cancelled) return;
        _launchLog.Info(settingsEditResult == SettingsEditResult.Imported ? "📥 Paramètres et profils importés" : "⚙️ Paramètres mis à jour");
        ReloadOptionChoices();
        UpdateToolAvailability();
        RefreshProjects();
        // Imported profiles replace the ones displayed for the current project.
        if (settingsEditResult == SettingsEditResult.Imported && CurrentProjectPath is { } projectPath)
            LoadProfilesForProject(projectPath, _projectScanner.DetectProject(projectPath));
        UpdateJumpList();
        SettingsApplied?.Invoke();
    }

    /// <summary>Raised once new settings are applied, so that the view applies the theme and the global shortcut again.</summary>
    public event Action? SettingsApplied;

    // ════════════════════════════════════════════════════════════
    //  AUTOMATION (MCP server)
    // ════════════════════════════════════════════════════════════

    /// <summary>Runs an automation request on the UI thread, where the state of the view model lives.</summary>
    private Task<T> RunAutomationAsync<T>(Func<Task<T>> automationAction) => _uiDispatcher.InvokeAsync(automationAction).Task.Unwrap();

    Task<IReadOnlyList<AutomationProject>> IDevLauncherAutomation.ListProjectsAsync() => RunAutomationAsync(() =>
    {
        IReadOnlyList<string> ReadProfileNames(string projectPath)
        {
            try
            {
                return _profileService.GetProfiles(projectPath).Select(profile => profile.Name).ToList();
            }
            catch (Exception exception) when (IsProfileStorageException(exception))
            {
                return Array.Empty<string>();
            }
        }
        var favoriteProjectPaths = _favoriteProjectsService.GetFavoriteProjectPaths();
        IReadOnlyList<AutomationProject> automationProjects = _projectEntries
            .Select(projectEntry => new AutomationProject(
                projectEntry.Name,
                projectEntry.Path,
                ProjectTypeLabels.GetName(_projectScanner.DetectProject(projectEntry.Path).ProjectType),
                ReadProfileNames(projectEntry.Path),
                favoriteProjectPaths.Any(favoritePath => PathComparer.AreSame(favoritePath, projectEntry.Path))))
            .ToList();
        return Task.FromResult(automationProjects);
    });

    Task<string> IDevLauncherAutomation.LaunchProjectAsync(string project, string? profileName)
        => RunAutomationAsync(() => HandleStartupCommandAsync(new StartupCommand(project, profileName, true, false)));

    Task<string> IDevLauncherAutomation.StopAllAsync() => RunAutomationAsync(async () =>
    {
        if (IsStopInProgress) return "⏳ Un arrêt est déjà en cours";
        await StopEnvironmentAsync();
        return StatusText;
    });

    Task<IReadOnlyList<AutomationService>> IDevLauncherAutomation.GetServiceStatusAsync() => RunAutomationAsync(() =>
    {
        IReadOnlyList<AutomationService> automationServices = ServiceIndicators
            .Select(indicator => new AutomationService(indicator.ServiceName, null, indicator.IsRunning, null))
            .Concat(LogTabs.OfType<ServiceLogTabViewModel>().Select(serviceTab => new AutomationService(
                serviceTab.Title,
                serviceTab.HostedService.ProjectName,
                serviceTab.IsRunning,
                serviceTab.HostedService.HasReadinessPattern ? serviceTab.HostedService.IsReady : null)))
            .ToList();
        return Task.FromResult(automationServices);
    });

    Task<IReadOnlyList<string>?> IDevLauncherAutomation.ReadServiceLogsAsync(string serviceName, int lineCount) => RunAutomationAsync(() =>
    {
        var logTab = serviceName.Trim().Equals("lancement", StringComparison.OrdinalIgnoreCase) ? LaunchLogTab : FindServiceLogTab(serviceName);
        return Task.FromResult(logTab?.GetLastLines(lineCount));
    });

    Task<string> IDevLauncherAutomation.RestartServiceAsync(string serviceName) => RunAutomationAsync(async () =>
    {
        if (FindServiceLogTab(serviceName) is not { } serviceTab) return $"❌ Aucun service « {serviceName} » lancé par DevLauncher : voir service_status";
        _launchLog.Info($"↻ Redémarrage de {serviceTab.Title} demandé par l'IA");
        await serviceTab.RestartCommand.ExecuteAsync(null);
        return serviceTab.IsRunning ? $"✅ {serviceTab.Title} redémarré" : $"❌ {serviceTab.Title} ne tourne pas après le redémarrage : voir read_service_logs";
    });

    /// <summary>The service tab named exactly like the request, or else the first one whose title contains it.</summary>
    private ServiceLogTabViewModel? FindServiceLogTab(string serviceName)
    {
        var serviceTabs = LogTabs.OfType<ServiceLogTabViewModel>().ToList();
        return serviceTabs.FirstOrDefault(serviceTab => serviceTab.Title.Equals(serviceName.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? serviceTabs.FirstOrDefault(serviceTab => serviceTab.Title.Contains(serviceName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    // ════════════════════════════════════════════════════════════
    //  LOG AND SERVICE STATUS
    // ════════════════════════════════════════════════════════════

    [RelayCommand]
    private void ClearLog() => (SelectedLogTab ?? LaunchLogTab).ClearLines();

    [RelayCommand]
    private void CopyLog() => _userInteractionService.CopyToClipboard((SelectedLogTab ?? LaunchLogTab).GetText());

    private void OnMessageLogged(string message, LogLevel level)
    {
        if (level == LogLevel.Detail && !AppSettings.DetailedLogging) return;
        RunOnUiThread(() => LaunchLogTab.AppendLine(level == LogLevel.Detail ? $"🔍 {message}" : message, level == LogLevel.Error));
    }

    /// <summary>Adds a log tab for each new service run by DevLauncher, and notifies when it stops by itself.</summary>
    private void OnServiceCreated(HostedService hostedService)
    {
        hostedService.Exited += (exitCode, isStopRequested) =>
        {
            if (isStopRequested) return;
            RunOnUiThread(() => _userInteractionService.ShowNotification("💥 Service arrêté",
                $"{hostedService.Command.Title} ({hostedService.ProjectName}) s'est arrêté de lui-même (code {exitCode})", isWarning: true, onlyWhenInBackground: false));
        };
        RunOnUiThread(() => LogTabs.Add(new ServiceLogTabViewModel(hostedService, _launchLog, RunOnUiThread, CloseServiceLogTab)));
    }

    private void CloseServiceLogTab(ServiceLogTabViewModel serviceLogTab)
    {
        if (SelectedLogTab == serviceLogTab) SelectedLogTab = LaunchLogTab;
        LogTabs.Remove(serviceLogTab);
        _serviceProcessHost.Remove(serviceLogTab.HostedService);
    }

    /// <summary>Updates the indicator, and notifies when a XAMPP service stops outside of « Tout arrêter ».</summary>
    private void OnServiceStatusChanged(string serviceName, bool isRunning) => RunOnUiThread(() =>
    {
        var serviceIndicator = ServiceIndicators.FirstOrDefault(indicator => indicator.ServiceName == serviceName);
        if (serviceIndicator is null) return;
        var hasStoppedUnexpectedly = serviceIndicator.IsRunning && !isRunning && !IsStopInProgress;
        serviceIndicator.IsRunning = isRunning;
        if (hasStoppedUnexpectedly)
            _userInteractionService.ShowNotification($"⚠️ {serviceName} s'est arrêté", $"{serviceName} ne tourne plus.", isWarning: true, onlyWhenInBackground: false);
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
