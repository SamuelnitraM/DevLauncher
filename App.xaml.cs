using System.Windows;
using DevLauncher.Services;
using DevLauncher.Services.Hosting;
using DevLauncher.Services.Stacks;
using DevLauncher.Services.Startup;
using DevLauncher.Services.Tools;
using DevLauncher.ViewModels;
using DevLauncher.Views;
using DevLauncher.Views.Shell;

namespace DevLauncher;

public partial class App : Application
{
    private MainViewModel? _mainViewModel;
    private PersistentLogWriter? _persistentLogWriter;
    private SingleInstanceCoordinator? _singleInstanceCoordinator;
    private TrayIcon? _trayIcon;
    private GlobalHotkey? _globalHotkey;

    /// <summary>
    /// Composition root : hands the request over to the running instance when there is one, otherwise prepares
    /// the data directory, loads the settings, builds the services and the main window, then runs the startup request.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var startupCommand = StartupCommand.Parse(e.Args);
        _singleInstanceCoordinator = new SingleInstanceCoordinator();
        // A running instance that does not answer is left alone : this one starts normally.
        if (!_singleInstanceCoordinator.TryBecomePrimaryInstance() && _singleInstanceCoordinator.TryForwardToPrimaryInstance(startupCommand.ToArguments()))
        {
            _singleInstanceCoordinator.Dispose();
            _singleInstanceCoordinator = null;
            Shutdown();
            return;
        }
        StoragePaths.InitializeDataDirectory();
        SettingsService.Load();
        ThemeService.Apply(AppSettings.Theme);
        var launchLog = new LaunchLog();
        _persistentLogWriter = new PersistentLogWriter(StoragePaths.LogsDirectory, launchLog, () => AppSettings.DetailedLogging);
        launchLog.Info($"⚡ DevLauncher {typeof(App).Assembly.GetName().Version} démarré");
        // Crashes are written to the persistent log before the application closes.
        DispatcherUnhandledException += (_, exceptionEventArgs) => launchLog.Error($"💥 Erreur inattendue : {exceptionEventArgs.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, exceptionEventArgs) => launchLog.Error($"💥 Erreur inattendue : {exceptionEventArgs.ExceptionObject}");
        var processEventWatcher = new ProcessEventWatcher();
        var processLauncher = new ProcessLauncher(launchLog);
        var toolCatalog = new ToolCatalog();
        var serviceProcessHost = new ServiceProcessHost(launchLog);
        var launchService = new LaunchService(
            toolCatalog,
            processLauncher,
            new VSCodeTasksServiceHost(processEventWatcher, launchLog),
            serviceProcessHost,
            launchLog);
        var mainWindow = new MainWindow();
        _trayIcon = new TrayIcon("DevLauncher");
        _mainViewModel = new MainViewModel(
            new ProjectScanner(),
            new ProfileService(),
            new RecentProjectsService(),
            new FavoriteProjectsService(),
            new GitStatusService(),
            new LaunchStatisticsService(),
            new VirtualHostService(),
            processLauncher,
            toolCatalog,
            launchService,
            serviceProcessHost,
            new ServiceMonitor(processEventWatcher),
            processEventWatcher,
            launchLog,
            new UserInteractionService(mainWindow, _trayIcon));
        mainWindow.DataContext = _mainViewModel;
        MainWindow = mainWindow;
        _ = new TrayController(_trayIcon, mainWindow, _mainViewModel);
        _globalHotkey = new GlobalHotkey(mainWindow);
        _globalHotkey.Pressed += () =>
        {
            mainWindow.BringToFront();
            _mainViewModel.OpenCommandPaletteCommand.Execute(null);
        };
        ApplyGlobalHotkey(launchLog);
        _mainViewModel.SettingsApplied += () =>
        {
            ThemeService.Apply(AppSettings.Theme);
            ApplyGlobalHotkey(launchLog);
        };
        RefreshMovedShellIntegration(launchLog);
        _singleInstanceCoordinator.ArgumentsReceived += forwardedArguments => Dispatcher.BeginInvoke(() => OnArgumentsForwarded(forwardedArguments));
        _singleInstanceCoordinator.StartListening();
        // Started minimized : the window stays in the notification area when the option allows it.
        if (!startupCommand.StartsMinimized || !AppSettings.MinimizeToTray) mainWindow.Show();
        if (startupCommand.StartsMinimized && !AppSettings.MinimizeToTray) mainWindow.WindowState = WindowState.Minimized;
        _ = _mainViewModel.HandleStartupCommandAsync(startupCommand);
    }

    /// <summary>Registers the global shortcut of the settings, empty meaning disabled.</summary>
    private void ApplyGlobalHotkey(LaunchLog launchLog)
    {
        if (_globalHotkey is null) return;
        if (string.IsNullOrWhiteSpace(AppSettings.GlobalHotkey))
        {
            _globalHotkey.Unregister();
            return;
        }
        if (!_globalHotkey.Register(AppSettings.GlobalHotkey))
            launchLog.Error($"⚠️ Raccourci global « {AppSettings.GlobalHotkey} » indisponible (invalide ou déjà pris par une autre application)");
    }

    /// <summary>A second instance was started (jump list, link, Explorer, command line) : this window comes forward and runs its request.</summary>
    private void OnArgumentsForwarded(IReadOnlyList<string> forwardedArguments)
    {
        if (MainWindow is MainWindow mainWindow) mainWindow.BringToFront();
        _ = _mainViewModel?.HandleStartupCommandAsync(StartupCommand.Parse(forwardedArguments));
    }

    /// <summary>Points the devlauncher:// protocol and the Explorer menu to this executable when DevLauncher was moved since their registration.</summary>
    private static void RefreshMovedShellIntegration(LaunchLog launchLog)
    {
        try
        {
            var windowsIntegrationService = new WindowsIntegrationService();
            if (!windowsIntegrationService.IsShellIntegrationOutdated()) return;
            windowsIntegrationService.RegisterShellIntegration();
            launchLog.Info("🔗 Protocole devlauncher:// et menu de l'Explorateur mis à jour vers cet exécutable");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            launchLog.Error($"⚠️ Mise à jour de l'intégration Windows impossible : {exception.Message}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceCoordinator?.Dispose();
        _globalHotkey?.Dispose();
        _trayIcon?.Dispose();
        _mainViewModel?.Dispose();
        _persistentLogWriter?.Dispose();
        base.OnExit(e);
    }
}
