using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace DevLauncher.Services.Startup;

/// <summary>
/// Keeps a single DevLauncher per Windows session : the first instance creates a named event, which exists as long as
/// it runs, and listens on a named pipe ; a second instance forwards its arguments through the pipe and exits.
/// The named event has no thread affinity, unlike a mutex : it can be released from any thread.
/// </summary>
public sealed class SingleInstanceCoordinator : IDisposable
{
    private const int AllowAnyProcess = -1;
    private static readonly TimeSpan ForwardingTimeout = TimeSpan.FromSeconds(5);

    private readonly string _instanceMarkerName;
    private readonly string _pipeName;
    private EventWaitHandle? _instanceMarker;
    private CancellationTokenSource? _listeningCancellation;

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    public SingleInstanceCoordinator() : this($"DevLauncher-{Environment.UserName}-{Process.GetCurrentProcess().SessionId}")
    {
    }

    public SingleInstanceCoordinator(string instanceName)
    {
        _instanceMarkerName = $@"Local\{instanceName}";
        _pipeName = instanceName;
    }

    /// <summary>Raised on a background thread with the arguments forwarded by a second instance.</summary>
    public event Action<IReadOnlyList<string>>? ArgumentsReceived;

    /// <summary>Returns true for the first instance of the session, which then holds the instance marker.</summary>
    public bool TryBecomePrimaryInstance()
    {
        var instanceMarker = new EventWaitHandle(false, EventResetMode.ManualReset, _instanceMarkerName, out var isCreatedNew);
        if (!isCreatedNew)
        {
            instanceMarker.Dispose();
            return false;
        }
        _instanceMarker = instanceMarker;
        return true;
    }

    /// <summary>Waits for the second instances in the background, one connection at a time, until disposal.</summary>
    public void StartListening()
    {
        _listeningCancellation = new CancellationTokenSource();
        _ = ListenAsync(_listeningCancellation.Token);
    }

    /// <summary>Sends the arguments to the first instance and lets it take the foreground. Returns false when it does not answer.</summary>
    public bool TryForwardToPrimaryInstance(IReadOnlyList<string> arguments)
    {
        try
        {
            if (OperatingSystem.IsWindows()) AllowSetForegroundWindow(AllowAnyProcess);
            using var pipeClient = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            pipeClient.Connect((int)ForwardingTimeout.TotalMilliseconds);
            var message = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(arguments));
            pipeClient.Write(message, 0, message.Length);
            pipeClient.Flush();
            return true;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipeServer;
            try
            {
                pipeServer = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The pipe name is held by another program : forwarding is unavailable for this session.
                return;
            }
            await using (pipeServer)
            {
                try
                {
                    await pipeServer.WaitForConnectionAsync(cancellationToken);
                    using var messageReader = new StreamReader(pipeServer, Encoding.UTF8);
                    var message = await messageReader.ReadToEndAsync(cancellationToken);
                    var forwardedArguments = JsonSerializer.Deserialize<List<string>>(message);
                    if (forwardedArguments is not null) ArgumentsReceived?.Invoke(forwardedArguments);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception exception) when (exception is IOException or JsonException)
                {
                    // A broken or malformed message is dropped : the next instance gets a fresh connection.
                }
            }
        }
    }

    public void Dispose()
    {
        _listeningCancellation?.Cancel();
        _listeningCancellation?.Dispose();
        _listeningCancellation = null;
        _instanceMarker?.Dispose();
        _instanceMarker = null;
    }
}
