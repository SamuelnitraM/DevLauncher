using System.Windows;
using DevLauncher.Services;

namespace DevLauncher;

public partial class App : Application
{
    /// <summary>Prepares the data directory and loads the persisted settings before the main window and its services are created.</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        StoragePaths.InitializeDataDirectory();
        SettingsService.Load();
        base.OnStartup(e);
    }
}
