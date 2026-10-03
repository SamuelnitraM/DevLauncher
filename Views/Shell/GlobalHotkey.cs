using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DevLauncher.Services;

namespace DevLauncher.Views.Shell;

/// <summary>Shortcut registered for the whole session : Windows sends it to DevLauncher whatever application has the focus.</summary>
public sealed class GlobalHotkey : IDisposable
{
    private const int HotkeyMessage = 0x0312;
    private const uint NoRepeatModifier = 0x4000;
    private const int HotkeyIdentifier = 0xD1;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int identifier, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int identifier);

    private readonly HwndSource _windowSource;
    private bool _isRegistered;

    public GlobalHotkey(Window ownerWindow)
    {
        _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(ownerWindow).EnsureHandle());
        _windowSource.AddHook(OnWindowMessage);
    }

    /// <summary>Raised on the UI thread when the shortcut is pressed.</summary>
    public event Action? Pressed;

    /// <summary>Replaces the registered shortcut. Returns false when the text is invalid or another application already uses the shortcut.</summary>
    public bool Register(string gestureText)
    {
        Unregister();
        if (!HotkeyGesture.TryParse(gestureText, out var hotkeyGesture) || hotkeyGesture is null) return false;
        _isRegistered = RegisterHotKey(_windowSource.Handle, HotkeyIdentifier, hotkeyGesture.Modifiers | NoRepeatModifier, hotkeyGesture.VirtualKey);
        return _isRegistered;
    }

    public void Unregister()
    {
        if (!_isRegistered) return;
        UnregisterHotKey(_windowSource.Handle, HotkeyIdentifier);
        _isRegistered = false;
    }

    private IntPtr OnWindowMessage(IntPtr windowHandle, int message, IntPtr wordParameter, IntPtr longParameter, ref bool isHandled)
    {
        if (message != HotkeyMessage || (int)wordParameter != HotkeyIdentifier) return IntPtr.Zero;
        isHandled = true;
        Pressed?.Invoke();
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _windowSource.RemoveHook(OnWindowMessage);
    }
}
