using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace DevLauncher.Services.Elevation;

/// <summary>
/// Starts a program with the rights of the standard user while DevLauncher runs as administrator : the token of the
/// Windows shell (the desktop, never elevated) is duplicated and given to the new process. Applications started this way
/// accept files dragged from the Explorer and do not show « Administrator » in their title.
/// </summary>
public static class UnelevatedProcessStarter
{
    private const uint ProcessQueryInformation = 0x0400;
    private const uint TokenDuplicate = 0x0002;
    private const uint MaximumAllowed = 0x02000000;
    private const int SecurityImpersonation = 2;
    private const int TokenPrimary = 1;
    private const uint CreateUnicodeEnvironment = 0x00000400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInformation
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2;
        public IntPtr Reserved2Pointer;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr ProcessHandle;
        public IntPtr ThreadHandle;
        public int ProcessId;
        public int ThreadId;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(IntPtr existingToken, uint desiredAccess, IntPtr tokenAttributes, int impersonationLevel, int tokenType, out IntPtr newToken);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessWithTokenW(IntPtr token, uint logonFlags, string? applicationName, StringBuilder commandLine,
        uint creationFlags, IntPtr environment, string? currentDirectory, ref StartupInformation startupInformation, out ProcessInformation processInformation);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>True when DevLauncher runs with the administrator rights : only then is a launch without elevation different.</summary>
    public static bool IsCurrentProcessElevated
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var currentIdentity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(currentIdentity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>Starts the program with the token of the shell. Throws Win32Exception when Windows refuses it (no shell running, missing privilege).</summary>
    public static void Start(string executable, IEnumerable<string> arguments, string? workingDirectory)
        => StartCommandLine(BuildCommandLine(executable, arguments), workingDirectory);

    /// <summary>Starts a whole command line (executable then arguments) with the token of the shell.</summary>
    public static void StartCommandLine(string commandLineText, string? workingDirectory)
    {
        var shellWindow = GetShellWindow();
        if (shellWindow == IntPtr.Zero) throw new Win32Exception("Bureau Windows introuvable : impossible de lancer sans élévation");
        GetWindowThreadProcessId(shellWindow, out var shellProcessId);
        var shellProcess = OpenProcess(ProcessQueryInformation, false, shellProcessId);
        if (shellProcess == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        var shellToken = IntPtr.Zero;
        var primaryToken = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(shellProcess, TokenDuplicate, out shellToken)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!DuplicateTokenEx(shellToken, MaximumAllowed, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out primaryToken)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var startupInformation = new StartupInformation { Size = Marshal.SizeOf<StartupInformation>() };
            var commandLine = new StringBuilder(commandLineText);
            if (!CreateProcessWithTokenW(primaryToken, 0, null, commandLine, CreateUnicodeEnvironment, IntPtr.Zero, workingDirectory, ref startupInformation, out var processInformation))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            CloseHandle(processInformation.ThreadHandle);
            CloseHandle(processInformation.ProcessHandle);
        }
        finally
        {
            if (primaryToken != IntPtr.Zero) CloseHandle(primaryToken);
            if (shellToken != IntPtr.Zero) CloseHandle(shellToken);
            CloseHandle(shellProcess);
        }
    }

    /// <summary>Joins the executable and the arguments with the quoting rules of the Windows command line parser.</summary>
    public static string BuildCommandLine(string executable, IEnumerable<string> arguments)
        => string.Join(' ', new[] { executable }.Concat(arguments).Select(QuoteArgument));

    public static string QuoteArgument(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return argument;
        var quotedArgument = new StringBuilder("\"");
        var backslashCount = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashCount++;
                continue;
            }
            // Backslashes are doubled only before a quote, which is escaped itself.
            quotedArgument.Append('\\', character == '"' ? backslashCount * 2 + 1 : backslashCount);
            backslashCount = 0;
            quotedArgument.Append(character);
        }
        quotedArgument.Append('\\', backslashCount * 2);
        quotedArgument.Append('"');
        return quotedArgument.ToString();
    }
}
