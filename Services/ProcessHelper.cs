using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace DevLauncher.Services;

/// <summary>
/// Shared helpers to query local processes.
/// </summary>
public static class ProcessHelper
{
    /// <summary>Returns the process name without its .exe extension, as exposed by Process.ProcessName.</summary>
    public static string NormalizeProcessName(string processName)
        => processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? processName[..^4] : processName;

    /// <summary>Returns true when at least one process with this name is running.</summary>
    public static bool IsProcessRunning(string processName) => GetProcessIds(processName).Count > 0;

    /// <summary>Returns the identifiers of the running processes with this name.</summary>
    public static HashSet<int> GetProcessIds(string processName)
    {
        var processes = Process.GetProcessesByName(NormalizeProcessName(processName));
        var processIds = processes.Select(process => process.Id).ToHashSet();
        foreach (var process in processes) process.Dispose();
        return processIds;
    }

    /// <summary>Returns the name of a running process, or null when it has already exited.</summary>
    public static string? TryGetProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Returns true when the executable of the process is located inside the given folder.</summary>
    public static bool IsExecutableInside(Process process, string folderPath)
    {
        try
        {
            var executablePath = process.MainModule?.FileName;
            var normalizedFolderPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath)) + Path.DirectorySeparatorChar;
            return executablePath is not null && executablePath.StartsWith(normalizedFolderPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }
}
