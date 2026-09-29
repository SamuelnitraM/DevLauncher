using System.Runtime.InteropServices;
using System.Text;

namespace DevLauncher.Services;

/// <summary>
/// Closes top-level windows gracefully (WM_CLOSE), so that applications can prompt to save their work.
/// </summary>
public static class NativeWindowService
{
    private const uint WindowCloseMessage = 0x0010;

    private delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr callbackParameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr callbackParameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr windowHandle, StringBuilder windowText, int maximumLength);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr windowHandle, uint message, IntPtr wordParameter, IntPtr longParameter);

    /// <summary>
    /// Requests the closing of every visible window owned by the given process whose title contains
    /// one of the given segments (title parts separated by " - "). Returns the number of windows requested to close.
    /// </summary>
    public static int CloseWindows(string processName, IReadOnlyCollection<string> titleSegments)
    {
        var ownerProcessIds = ProcessHelper.GetProcessIds(processName);
        if (ownerProcessIds.Count == 0) return 0;
        var closeRequestedWindowCount = 0;
        EnumWindows((windowHandle, _) =>
        {
            if (!IsWindowVisible(windowHandle)) return true;
            GetWindowThreadProcessId(windowHandle, out var ownerProcessId);
            if (!ownerProcessIds.Contains((int)ownerProcessId)) return true;
            if (!TitleContainsSegment(GetWindowTitle(windowHandle), titleSegments)) return true;
            if (PostMessage(windowHandle, WindowCloseMessage, IntPtr.Zero, IntPtr.Zero)) closeRequestedWindowCount++;
            return true;
        }, IntPtr.Zero);
        return closeRequestedWindowCount;
    }

    private static string GetWindowTitle(IntPtr windowHandle)
    {
        var titleLength = GetWindowTextLength(windowHandle);
        if (titleLength == 0) return string.Empty;
        var titleBuilder = new StringBuilder(titleLength + 1);
        GetWindowText(windowHandle, titleBuilder, titleBuilder.Capacity);
        return titleBuilder.ToString();
    }

    /// <summary>Matches "segment", "● segment" or "segment (suffix)" among the title parts.</summary>
    private static bool TitleContainsSegment(string windowTitle, IReadOnlyCollection<string> titleSegments)
        => windowTitle
            .Split(" - ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(titlePart => titlePart.TrimStart('●', ' '))
            .Any(titlePart => titleSegments.Any(segment =>
                titlePart.Equals(segment, StringComparison.OrdinalIgnoreCase)
                || titlePart.StartsWith(segment + " (", StringComparison.OrdinalIgnoreCase)));
}
