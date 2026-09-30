using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DevLauncher.Services.Hosting;

/// <summary>
/// Windows job object killing its processes when its last handle closes, that is when DevLauncher exits for any reason
/// (normal closing, crash, debugger stop). Child processes started afterwards by a member belong to the job too.
/// </summary>
public sealed class KillOnCloseJob : IDisposable
{
    private const int JobObjectExtendedLimitInformationClass = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr jobAttributes, string? jobName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr jobHandle, int informationClass, ref JobObjectExtendedLimitInformation information, uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr jobHandle, IntPtr processHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    private IntPtr _jobHandle;

    public KillOnCloseJob()
    {
        _jobHandle = CreateJobObject(IntPtr.Zero, null);
        if (_jobHandle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        var extendedLimitInformation = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation { LimitFlags = JobObjectLimitKillOnJobClose },
        };
        var informationLength = (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        if (SetInformationJobObject(_jobHandle, JobObjectExtendedLimitInformationClass, ref extendedLimitInformation, informationLength)) return;
        var lastError = Marshal.GetLastWin32Error();
        Dispose();
        throw new Win32Exception(lastError);
    }

    /// <summary>Adds a process to the job. Returns false when Windows refuses it.</summary>
    public bool TryAssign(Process process)
    {
        try
        {
            return _jobHandle != IntPtr.Zero && AssignProcessToJobObject(_jobHandle, process.Handle);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_jobHandle == IntPtr.Zero) return;
        CloseHandle(_jobHandle);
        _jobHandle = IntPtr.Zero;
    }
}
