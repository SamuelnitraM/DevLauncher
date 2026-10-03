using System.IO;
using System.Text;

namespace DevLauncher.Services;

/// <summary>
/// Writes the launch log, and the service output when the detailed log is enabled, into one file per day
/// in the logs folder of the data directory. The oldest files beyond the retention count are deleted.
/// </summary>
public sealed class PersistentLogWriter : IDisposable
{
    public const int RetainedLogFileCount = 10;
    private const string LogFilePrefix = "devlauncher-";

    private readonly string _logsDirectory;
    private readonly LaunchLog _launchLog;
    private readonly Func<bool> _isDetailedLogEnabled;
    private readonly Func<DateTime> _getCurrentTime;
    private readonly object _writerLock = new();
    private StreamWriter? _logFileWriter;
    private DateOnly _logFileDate;
    private bool _isWritingDisabled;

    public PersistentLogWriter(string logsDirectory, LaunchLog launchLog, Func<bool> isDetailedLogEnabled, Func<DateTime>? getCurrentTime = null)
    {
        _logsDirectory = logsDirectory;
        _launchLog = launchLog;
        _isDetailedLogEnabled = isDetailedLogEnabled;
        _getCurrentTime = getCurrentTime ?? (() => DateTime.Now);
        _launchLog.MessageLogged += OnMessageLogged;
        _launchLog.ServiceOutputLogged += OnServiceOutputLogged;
    }

    /// <summary>Returns the path of the log file of a given day.</summary>
    public static string GetLogFilePath(string logsDirectory, DateOnly logDate) => Path.Combine(logsDirectory, $"{LogFilePrefix}{logDate:yyyy-MM-dd}.log");

    private void OnMessageLogged(string message, LogLevel level)
    {
        if (level == LogLevel.Detail && !_isDetailedLogEnabled()) return;
        var levelMarker = level switch
        {
            LogLevel.Error => "ERR",
            LogLevel.Detail => "DBG",
            _ => "INF",
        };
        WriteLine($"{levelMarker} {message}");
    }

    private void OnServiceOutputLogged(string serviceTitle, string outputLine, bool isError)
    {
        if (!_isDetailedLogEnabled()) return;
        WriteLine($"{(isError ? "ERR" : "OUT")} [{serviceTitle}] {outputLine}");
    }

    /// <summary>Appends a timestamped line, switching to the file of the new day when the date changes. A failing disk disables the writing.</summary>
    private void WriteLine(string text)
    {
        lock (_writerLock)
        {
            if (_isWritingDisabled) return;
            var currentTime = _getCurrentTime();
            try
            {
                var logWriter = GetWriter(DateOnly.FromDateTime(currentTime));
                logWriter.WriteLine($"{currentTime:HH:mm:ss.fff} {text}");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _isWritingDisabled = true;
                _logFileWriter?.Dispose();
                _logFileWriter = null;
            }
        }
    }

    /// <summary>Must be called under the writer lock.</summary>
    private StreamWriter GetWriter(DateOnly logDate)
    {
        if (_logFileWriter is not null && _logFileDate == logDate) return _logFileWriter;
        _logFileWriter?.Dispose();
        Directory.CreateDirectory(_logsDirectory);
        var logFileStream = new FileStream(GetLogFilePath(_logsDirectory, logDate), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        _logFileWriter = new StreamWriter(logFileStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
        _logFileDate = logDate;
        DeleteOldLogFiles(_logsDirectory, RetainedLogFileCount);
        return _logFileWriter;
    }

    /// <summary>Keeps the most recent log files only. The file names hold the date, so their order is the chronological order.</summary>
    public static void DeleteOldLogFiles(string logsDirectory, int retainedLogFileCount)
    {
        try
        {
            var oldLogFilePaths = Directory.EnumerateFiles(logsDirectory, $"{LogFilePrefix}*.log")
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                .Skip(retainedLogFileCount)
                .ToList();
            foreach (var oldLogFilePath in oldLogFilePaths) File.Delete(oldLogFilePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An old log file still opened elsewhere is deleted on a later day.
        }
    }

    public void Dispose()
    {
        _launchLog.MessageLogged -= OnMessageLogged;
        _launchLog.ServiceOutputLogged -= OnServiceOutputLogged;
        lock (_writerLock)
        {
            _logFileWriter?.Dispose();
            _logFileWriter = null;
            _isWritingDisabled = true;
        }
    }
}
