using System.Runtime.InteropServices;

namespace DevLauncher.Services;

/// <summary>
/// Finds the process listening on a local TCP port, from the TCP tables of Windows (IPv4 and IPv6).
/// </summary>
public static class PortOwnerLocator
{
    private const int AddressFamilyIPv4 = 2;
    private const int AddressFamilyIPv6 = 23;
    private const int TcpTableOwnerPidListener = 3;
    private const uint NoError = 0;
    private const uint InsufficientBuffer = 122;

    // Layout of MIB_TCPROW_OWNER_PID and MIB_TCP6ROW_OWNER_PID : row size, offset of the local port, offset of the owner process id.
    private const int IPv4RowSize = 24;
    private const int IPv4LocalPortOffset = 8;
    private const int IPv4OwnerProcessIdOffset = 20;
    private const int IPv6RowSize = 56;
    private const int IPv6LocalPortOffset = 20;
    private const int IPv6OwnerProcessIdOffset = 52;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr tcpTable, ref int tableSize, bool isSorted, int addressFamily, int tableClass, uint reserved);

    /// <summary>Returns the identifier of the process listening on the port, or null when the port is free or the tables cannot be read.</summary>
    public static int? FindListeningProcessId(int port)
        => FindListeningProcessId(port, AddressFamilyIPv4, IPv4RowSize, IPv4LocalPortOffset, IPv4OwnerProcessIdOffset)
           ?? FindListeningProcessId(port, AddressFamilyIPv6, IPv6RowSize, IPv6LocalPortOffset, IPv6OwnerProcessIdOffset);

    private static int? FindListeningProcessId(int port, int addressFamily, int rowSize, int localPortOffset, int ownerProcessIdOffset)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var tableSize = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref tableSize, false, addressFamily, TcpTableOwnerPidListener, 0);
        // The table can grow between the size query and the read : the read is retried with the new size.
        for (var attempt = 0; attempt < 3 && tableSize > 0; attempt++)
        {
            var tableBuffer = Marshal.AllocHGlobal(tableSize);
            try
            {
                var result = GetExtendedTcpTable(tableBuffer, ref tableSize, false, addressFamily, TcpTableOwnerPidListener, 0);
                if (result == InsufficientBuffer) continue;
                if (result != NoError) return null;
                var rowCount = Marshal.ReadInt32(tableBuffer);
                for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
                {
                    var rowOffset = sizeof(int) + rowIndex * rowSize;
                    var networkOrderPort = (uint)Marshal.ReadInt32(tableBuffer, rowOffset + localPortOffset);
                    if (ConvertNetworkOrderPort(networkOrderPort) == port) return Marshal.ReadInt32(tableBuffer, rowOffset + ownerProcessIdOffset);
                }
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(tableBuffer);
            }
        }
        return null;
    }

    /// <summary>The port is stored in network byte order in the two low bytes.</summary>
    public static int ConvertNetworkOrderPort(uint networkOrderPort) => (int)(((networkOrderPort & 0xFF) << 8) | ((networkOrderPort >> 8) & 0xFF));
}
