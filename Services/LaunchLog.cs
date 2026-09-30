namespace DevLauncher.Services;

/// <summary>
/// Shared launch log : every service writes here, the UI listens to MessageLogged.
/// </summary>
public sealed class LaunchLog
{
    /// <summary>Raised on the calling thread. Parameters : message, is it an error ?</summary>
    public event Action<string, bool>? MessageLogged;

    public void Info(string message) => MessageLogged?.Invoke(message, false);

    public void Error(string message) => MessageLogged?.Invoke(message, true);
}
