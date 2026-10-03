using System.Windows;
using DevLauncher.Services;
using DevLauncher.Services.Hosting;
using DevLauncher.Services.Tools;
using DevLauncher.ViewModels;
using DevLauncher.Views;

namespace DevLauncher;

public partial class App : Application
{
    private MainViewModel? _mainViewModel;
    private PersistentLogWriter? _persistentLogWriter;

    /// <summary>Composition root : prepares the data directory, loads the settings, then builds the services and the main window.</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
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
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mainViewModel?.Dispose();
        _persistentLogWriter?.Dispose();
        base.OnExit(e);
    }
}
