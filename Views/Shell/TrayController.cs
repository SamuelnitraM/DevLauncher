using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using DevLauncher.Services.Startup;
using DevLauncher.ViewModels;

namespace DevLauncher.Views.Shell;

/// <summary>
/// Behavior of the notification area icon : a click shows the window, a right click opens the quick menu
/// (launch a favorite or recent project, stop everything, quit), and a minimized window hides in the notification area.
/// </summary>
public sealed class TrayController
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    private readonly TrayIcon _trayIcon;
    private readonly MainWindow _mainWindow;
    private readonly MainViewModel _mainViewModel;

    public TrayController(TrayIcon trayIcon, MainWindow mainWindow, MainViewModel mainViewModel)
    {
        _trayIcon = trayIcon;
        _mainWindow = mainWindow;
        _mainViewModel = mainViewModel;
        _trayIcon.LeftClicked += _mainWindow.BringToFront;
        _trayIcon.NotificationClicked += _mainWindow.BringToFront;
        _trayIcon.RightClicked += ShowQuickMenu;
        _mainWindow.StateChanged += OnMainWindowStateChanged;
    }

    /// <summary>With the option enabled, minimizing hides the window : the notification area icon brings it back.</summary>
    private void OnMainWindowStateChanged(object? sender, EventArgs e)
    {
        if (_mainWindow.WindowState == WindowState.Minimized && AppSettings.MinimizeToTray) _mainWindow.Hide();
    }

    private void ShowQuickMenu()
    {
        var quickMenu = new ContextMenu { Placement = PlacementMode.MousePoint };
        quickMenu.Items.Add(CreateMenuItem("🪟 Ouvrir DevLauncher", _mainWindow.BringToFront));
        quickMenu.Items.Add(CreateMenuItem("⚡ Palette de commandes", () =>
        {
            _mainWindow.BringToFront();
            _mainViewModel.OpenCommandPaletteCommand.Execute(null);
        }));
        quickMenu.Items.Add(new Separator());
        var quickLaunchProjects = _mainViewModel.GetQuickLaunchProjects();
        if (quickLaunchProjects.Count == 0)
            quickMenu.Items.Add(new MenuItem { Header = "Aucun projet favori ou récent", IsEnabled = false });
        foreach (var projectEntry in quickLaunchProjects)
        {
            quickMenu.Items.Add(CreateMenuItem($"🚀 Lancer {projectEntry.Name}",
                () => _ = _mainViewModel.HandleStartupCommandAsync(new StartupCommand(projectEntry.Path, null, true, false)),
                _mainViewModel.IsIdle));
        }
        quickMenu.Items.Add(new Separator());
        quickMenu.Items.Add(CreateMenuItem("⏹ Tout arrêter", () => _mainViewModel.StopAllCommand.Execute(null), _mainViewModel.StopAllCommand.CanExecute(null)));
        quickMenu.Items.Add(CreateMenuItem("✖ Quitter", () =>
        {
            _mainWindow.BringToFront();
            _mainWindow.Close();
        }));
        // The menu window must be in the foreground, otherwise a click elsewhere does not close it.
        quickMenu.Opened += (_, _) =>
        {
            if (PresentationSource.FromVisual(quickMenu) is HwndSource menuSource) SetForegroundWindow(menuSource.Handle);
        };
        quickMenu.IsOpen = true;
    }

    private static MenuItem CreateMenuItem(string header, Action onClick, bool isEnabled = true)
    {
        var menuItem = new MenuItem { Header = header, IsEnabled = isEnabled };
        menuItem.Click += (_, _) => onClick();
        return menuItem;
    }
}
