using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace DevLauncher.Services;

/// <summary>
/// Readiness probe of a local server : a port is ready when it accepts a TCP connection.
/// </summary>
public static class PortProbe
{
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(1);

    public static async Task<bool> IsPortOpenAsync(int port)
    {
        using var tcpClient = new TcpClient();
        using var connectionCancellation = new CancellationTokenSource(ConnectionTimeout);
        try
        {
            await tcpClient.ConnectAsync(IPAddress.Loopback, port, connectionCancellation.Token);
            return true;
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Retries a local TCP connection until the port accepts it or the timeout expires.</summary>
    public static async Task<bool> WaitForPortAsync(int port, TimeSpan timeout)
    {
        var readinessStopwatch = Stopwatch.StartNew();
        while (readinessStopwatch.Elapsed < timeout)
        {
            if (await IsPortOpenAsync(port)) return true;
            await Task.Delay(ProbeInterval);
        }
        return false;
    }
}
