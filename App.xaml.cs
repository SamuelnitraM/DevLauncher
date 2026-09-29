using System.Windows;
using DevLauncher.Services;

namespace DevLauncher;

public partial class App : Application
{
    /// <summary>Loads the persisted settings before the main window and its services are created.</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        SettingsService.Load();
        base.OnStartup(e);
    }
}
