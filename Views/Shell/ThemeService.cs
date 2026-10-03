using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace DevLauncher.Views.Shell;

/// <summary>
/// Applies the color theme : light or dark, or the one of Windows (followed live). The colors are the first merged
/// dictionary of the application, swapped at once ; the title bars of the windows follow through the window manager.
/// </summary>
public static class ThemeService
{
    private const int ImmersiveDarkModeAttribute = 20;
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static string _themeSetting = ThemeNames.System;
    private static bool _isInitialized;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int attributeValue, int attributeSize);

    /// <summary>True while the dark colors are applied.</summary>
    public static bool IsDarkThemeActive { get; private set; } = true;

    /// <summary>Applies the theme setting (system, light or dark) to the application and to every window.</summary>
    public static void Apply(string themeSetting)
    {
        _themeSetting = themeSetting;
        if (!_isInitialized)
        {
            _isInitialized = true;
            // Every window gets the title bar of the theme as soon as it is shown.
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) => ApplyTitleBar((Window)sender)));
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
        var isDarkTheme = themeSetting switch
        {
            ThemeNames.Light => false,
            ThemeNames.Dark => true,
            _ => IsWindowsAppThemeDark(),
        };
        IsDarkThemeActive = isDarkTheme;
        var applicationResources = Application.Current.Resources;
        var themeDictionary = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/{(isDarkTheme ? "Dark" : "Light")}Theme.xaml") };
        if (applicationResources.MergedDictionaries.Count > 0) applicationResources.MergedDictionaries[0] = themeDictionary;
        else applicationResources.MergedDictionaries.Add(themeDictionary);
        foreach (Window openWindow in Application.Current.Windows) ApplyTitleBar(openWindow);
    }

    /// <summary>Reads the « app mode » chosen in the Windows settings. Dark when it cannot be read.</summary>
    public static bool IsWindowsAppThemeDark()
    {
        try
        {
            using var personalizeKey = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            return personalizeKey?.GetValue("AppsUseLightTheme") is not int usesLightTheme || usesLightTheme == 0;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>The Windows color settings changed : the system theme is applied again.</summary>
    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General || _themeSetting != ThemeNames.System) return;
        Application.Current?.Dispatcher.BeginInvoke(() => Apply(_themeSetting));
    }

    private static void ApplyTitleBar(Window window)
    {
        var windowHandle = new WindowInteropHelper(window).Handle;
        if (windowHandle == IntPtr.Zero) return;
        var useDarkTitleBar = IsDarkThemeActive ? 1 : 0;
        DwmSetWindowAttribute(windowHandle, ImmersiveDarkModeAttribute, ref useDarkTitleBar, sizeof(int));
    }
}
