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

    /// <summary>Composition root : prepares the data directory, loads the settings, then builds the services and the main window.</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        StoragePaths.InitializeDataDirectory();
        SettingsService.Load();
        var launchLog = new LaunchLog();
        var processEventWatcher = new ProcessEventWatcher();
        var processLauncher = new ProcessLauncher(launchLog);
        var toolCatalog = new ToolCatalog();
        var launchService = new LaunchService(
            toolCatalog,
            processLauncher,
            new VSCodeTasksServiceHost(processEventWatcher, launchLog),
            new WindowsTerminalServiceHost(processLauncher, launchLog),
            launchLog);
        var mainWindow = new MainWindow();
        _mainViewModel = new MainViewModel(
            new ProjectScanner(),
            new ProfileService(),
            new RecentProjectsService(),
            toolCatalog,
            launchService,
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
        base.OnExit(e);
    }
}
