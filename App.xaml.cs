using System.Windows;
using DevLauncher.Services;
using DevLauncher.Services.Hosting;
using DevLauncher.Services.Startup;
using DevLauncher.Services.Tools;
using DevLauncher.ViewModels;
using DevLauncher.Views;

namespace DevLauncher;

public partial class App : Application
{
    private MainViewModel? _mainViewModel;
    private PersistentLogWriter? _persistentLogWriter;
    private SingleInstanceCoordinator? _singleInstanceCoordinator;

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
        _mainViewModel = new MainViewModel(
            new ProjectScanner(),
            new ProfileService(),
            new RecentProjectsService(),
            new FavoriteProjectsService(),
            new GitStatusService(),
            new LaunchStatisticsService(),
            processLauncher,
            toolCatalog,
            launchService,
            serviceProcessHost,
            new ServiceMonitor(processEventWatcher),
            processEventWatcher,
            launchLog,
            new UserInteractionService(mainWindow));
        mainWindow.DataContext = _mainViewModel;
        MainWindow = mainWindow;
        RefreshMovedShellIntegration(launchLog);
        _singleInstanceCoordinator.ArgumentsReceived += forwardedArguments => Dispatcher.BeginInvoke(() => OnArgumentsForwarded(forwardedArguments));
        _singleInstanceCoordinator.StartListening();
        if (startupCommand.StartsMinimized) mainWindow.WindowState = WindowState.Minimized;
        mainWindow.Show();
        _ = _mainViewModel.HandleStartupCommandAsync(startupCommand);
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
        _mainViewModel?.Dispose();
        _persistentLogWriter?.Dispose();
        base.OnExit(e);
    }
}
