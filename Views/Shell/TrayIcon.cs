using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DevLauncher.Views.Shell;

/// <summary>
/// Icon of the notification area, with its notifications (shown as Windows notifications).
/// Its messages arrive on a hidden window, which also restores the icon when the Explorer restarts.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const int CallbackMessage = 0x8000 + 1;
    private const int LeftButtonUp = 0x0202;
    private const int RightButtonUp = 0x0205;
    private const int BalloonUserClick = 0x0405;
    private const uint AddIcon = 0x0;
    private const uint ModifyIcon = 0x1;
    private const uint DeleteIcon = 0x2;
    private const uint MessageFlag = 0x1;
    private const uint IconFlag = 0x2;
    private const uint TipFlag = 0x4;
    private const uint InfoFlag = 0x10;
    private const uint InfoIconInformation = 0x1;
    private const uint InfoIconWarning = 0x2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int Size;
        public IntPtr WindowHandle;
        public uint Identifier;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr IconHandle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;
        public uint InfoFlags;
        public Guid ItemGuid;
        public IntPtr BalloonIconHandle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData notifyIconData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string fileName, int iconIndex, IntPtr[]? largeIcons, IntPtr[] smallIcons, uint iconCount);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr iconHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string messageName);

    private readonly HwndSource _messageWindow;
    private readonly IntPtr _iconHandle;
    private readonly uint _taskbarCreatedMessage;
    private readonly string _tooltip;
    private bool _isAdded;

    public TrayIcon(string tooltip)
    {
        _tooltip = tooltip;
        // A hidden top-level window : unlike a message-only window, it receives the TaskbarCreated broadcast.
        _messageWindow = new HwndSource(new HwndSourceParameters("DevLauncherTrayIcon") { Width = 0, Height = 0, WindowStyle = 0 });
        _messageWindow.AddHook(OnWindowMessage);
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        var smallIcons = new IntPtr[1];
        if (Environment.ProcessPath is { } executablePath && ExtractIconEx(executablePath, 0, null, smallIcons, 1) > 0) _iconHandle = smallIcons[0];
        AddToNotificationArea();
    }

    /// <summary>Raised on the UI thread by a left click on the icon.</summary>
    public event Action? LeftClicked;

    /// <summary>Raised on the UI thread by a right click on the icon.</summary>
    public event Action? RightClicked;

    /// <summary>Raised on the UI thread by a click on a notification.</summary>
    public event Action? NotificationClicked;

    /// <summary>Shows a Windows notification coming from the icon.</summary>
    public void ShowNotification(string title, string message, bool isWarning)
    {
        if (!_isAdded) return;
        var notifyIconData = CreateNotifyIconData(InfoFlag);
        notifyIconData.InfoTitle = Truncate(title, 63);
        notifyIconData.Info = Truncate(message, 255);
        notifyIconData.InfoFlags = isWarning ? InfoIconWarning : InfoIconInformation;
        Shell_NotifyIcon(ModifyIcon, ref notifyIconData);
    }

    private void AddToNotificationArea()
    {
        var notifyIconData = CreateNotifyIconData(MessageFlag | IconFlag | TipFlag);
        _isAdded = Shell_NotifyIcon(AddIcon, ref notifyIconData);
    }

    private NotifyIconData CreateNotifyIconData(uint flags) => new()
    {
        Size = Marshal.SizeOf<NotifyIconData>(),
        WindowHandle = _messageWindow.Handle,
        Identifier = 1,
        Flags = flags,
        CallbackMessage = CallbackMessage,
        IconHandle = _iconHandle,
        Tip = Truncate(_tooltip, 127),
        Info = string.Empty,
        InfoTitle = string.Empty,
    };

    private IntPtr OnWindowMessage(IntPtr windowHandle, int message, IntPtr wordParameter, IntPtr longParameter, ref bool isHandled)
    {
        if (message == CallbackMessage)
        {
            switch ((int)longParameter & 0xFFFF)
            {
                case LeftButtonUp:
                    LeftClicked?.Invoke();
                    break;
                case RightButtonUp:
                    RightClicked?.Invoke();
                    break;
                case BalloonUserClick:
                    NotificationClicked?.Invoke();
                    break;
            }
            isHandled = true;
        }
        else if (_taskbarCreatedMessage != 0 && message == (int)_taskbarCreatedMessage)
        {
            AddToNotificationArea();
        }
        return IntPtr.Zero;
    }

    private static string Truncate(string text, int maximumLength) => text.Length <= maximumLength ? text : text[..(maximumLength - 1)] + "…";

    public void Dispose()
    {
        if (_isAdded)
        {
            var notifyIconData = CreateNotifyIconData(0);
            Shell_NotifyIcon(DeleteIcon, ref notifyIconData);
            _isAdded = false;
        }
        if (_iconHandle != IntPtr.Zero) DestroyIcon(_iconHandle);
        _messageWindow.RemoveHook(OnWindowMessage);
        _messageWindow.Dispose();
    }
}
