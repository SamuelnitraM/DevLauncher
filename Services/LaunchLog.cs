namespace DevLauncher.Services;

/// <summary>Importance of a log message. Detail messages are shown and saved only when the detailed log is enabled.</summary>
public enum LogLevel
{
    Detail,
    Info,
    Error,
}

/// <summary>
/// Shared launch log : every service writes here, the UI and the persistent log listen to MessageLogged.
/// </summary>
public sealed class LaunchLog
{
    /// <summary>Raised on the calling thread. Parameters : message, level.</summary>
    public event Action<string, LogLevel>? MessageLogged;

    /// <summary>Raised on the calling thread for each output line of a service. Parameters : service title, line, is it an error ?</summary>
    public event Action<string, string, bool>? ServiceOutputLogged;

    public void Detail(string message) => MessageLogged?.Invoke(message, LogLevel.Detail);

    public void Info(string message) => MessageLogged?.Invoke(message, LogLevel.Info);

    public void Error(string message) => MessageLogged?.Invoke(message, LogLevel.Error);

    public void ServiceOutput(string serviceTitle, string outputLine, bool isError) => ServiceOutputLogged?.Invoke(serviceTitle, outputLine, isError);
}
