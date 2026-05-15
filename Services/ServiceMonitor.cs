using System.Diagnostics;
using System.Timers;
using Timer = System.Timers.Timer;

namespace DevLauncher.Services;

/// <summary>
/// Vérifie périodiquement si Apache et MySQL tournent,
/// et émet un événement StatusChanged pour mettre à jour l'UI.
/// </summary>
public class ServiceMonitor : IDisposable
{
    private readonly Timer _timer = new();

    /// <summary>
    /// Émis quand le statut d'un service change.
    /// Paramètres : nom du service ("Apache" | "MySQL"), est-il actif ?
    /// </summary>
    public event Action<string, bool>? StatusChanged;

    private bool _lastApache;
    private bool _lastMySQL;
    private bool _lastFileZilla;

    public void Start(TimeSpan interval)
    {
        _timer.Interval = interval.TotalMilliseconds;
        _timer.Elapsed += OnTick;
        _timer.AutoReset = true;
        _timer.Start();

        // Vérification immédiate au démarrage
        CheckAll();
    }

    public void Stop() => _timer.Stop();

    private void OnTick(object? sender, ElapsedEventArgs e) => CheckAll();

    private void CheckAll()
    {
        bool apacheRunning = IsProcessRunning("httpd");
        bool mysqlRunning = IsProcessRunning("mysqld");
        bool filezillaRunning = IsProcessRunning("FileZillaServer");

        if (apacheRunning != _lastApache)
        {
            _lastApache = apacheRunning;
            StatusChanged?.Invoke("Apache", apacheRunning);
        }

        if (mysqlRunning != _lastMySQL)
        {
            _lastMySQL = mysqlRunning;
            StatusChanged?.Invoke("MySQL", mysqlRunning);
        }

        if (filezillaRunning != _lastFileZilla)
        {
            _lastFileZilla = filezillaRunning;
            StatusChanged?.Invoke("FileZilla", filezillaRunning);
        }
    }

    /// <summary>
    /// Vérifie si un processus Windows portant ce nom est actif.
    /// Apache = "httpd", MySQL = "mysqld"
    /// </summary>
    private static bool IsProcessRunning(string processName)
    {
        try
        {
            return Process.GetProcessesByName(processName).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        GC.SuppressFinalize(this);
    }
}
