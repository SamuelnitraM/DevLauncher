using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevLauncher.Models;
using DevLauncher.Services;
using DevLauncher.Services.Hosting;

namespace DevLauncher.ViewModels;

/// <summary>Tab of the log area : the launch log, or the output of a service.</summary>
public partial class LogTabViewModel : ObservableObject
{
    private const int MaximumLineCount = 5000;

    private readonly object _linesLock = new();

    /// <summary>Must be created on the UI thread : the lines are registered for synchronized access by the bindings.</summary>
    public LogTabViewModel(string title)
    {
        Title = title;
        BindingOperations.EnableCollectionSynchronization(Lines, _linesLock);
        VisibleLines = CollectionViewSource.GetDefaultView(Lines);
        VisibleLines.Filter = IsLineVisible;
    }

    public string Title { get; }
    public ObservableCollection<LogEntry> Lines { get; } = new();

    /// <summary>Lines shown in the tab : all of them, or those matching the search and the error filter.</summary>
    public ICollectionView VisibleLines { get; }

    public virtual bool IsService => false;

    /// <summary>Text searched in the lines, case-insensitive. Empty shows every line.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFiltered))]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFiltered))]
    private bool _showsErrorsOnly;

    public bool IsFiltered => ShowsErrorsOnly || !string.IsNullOrWhiteSpace(SearchText);

    /// <summary>Number of lines shown out of the total, displayed while a filter is active.</summary>
    public string FilterSummary
    {
        get
        {
            lock (_linesLock) return $"{Lines.Count(IsLineVisible)} / {Lines.Count} ligne(s)";
        }
    }

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    partial void OnShowsErrorsOnlyChanged(bool value) => RefreshFilter();

    private void RefreshFilter()
    {
        VisibleLines.Refresh();
        OnPropertyChanged(nameof(FilterSummary));
    }

    private bool IsLineVisible(object line)
        => line is LogEntry logEntry
           && (!ShowsErrorsOnly || logEntry.IsError)
           && (string.IsNullOrWhiteSpace(SearchText) || logEntry.Text.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase));

    [ObservableProperty]
    private bool _isRunning;

    partial void OnIsRunningChanged(bool value) => OnRunningStateChanged();

    protected virtual void OnRunningStateChanged()
    {
    }

    /// <summary>Appends a timestamped line, with its colors when it comes from a service.</summary>
    public void AppendLine(string text, bool isError, IReadOnlyList<AnsiSegment>? segments = null)
    {
        lock (_linesLock)
        {
            Lines.Add(new LogEntry($"{DateTime.Now:HH:mm:ss}", text, isError, segments));
            while (Lines.Count > MaximumLineCount) Lines.RemoveAt(0);
        }
        if (IsFiltered) OnPropertyChanged(nameof(FilterSummary));
    }

    public void ClearLines()
    {
        lock (_linesLock) Lines.Clear();
        OnPropertyChanged(nameof(FilterSummary));
    }

    /// <summary>Text of the last lines, whatever the filter.</summary>
    public IReadOnlyList<string> GetLastLines(int lineCount)
    {
        lock (_linesLock) return Lines.Skip(Math.Max(0, Lines.Count - lineCount)).Select(logEntry => logEntry.Text).ToList();
    }

    /// <summary>Text of the lines shown : the filtered lines when a filter is active.</summary>
    public string GetText()
    {
        lock (_linesLock) return string.Join(Environment.NewLine, Lines.Where(IsLineVisible).Select(logEntry => logEntry.Text));
    }
}

/// <summary>Tab showing the output of a service run by DevLauncher, with its stop and restart actions.</summary>
public partial class ServiceLogTabViewModel : LogTabViewModel
{
    private readonly HostedService _hostedService;
    private readonly LaunchLog _launchLog;
    private readonly Action<Action> _runOnUiThread;
    private readonly Action<ServiceLogTabViewModel> _closeTab;

    public ServiceLogTabViewModel(HostedService hostedService, LaunchLog launchLog, Action<Action> runOnUiThread, Action<ServiceLogTabViewModel> closeTab)
        : base($"{hostedService.Command.Title} · {hostedService.ProjectName}")
    {
        _hostedService = hostedService;
        _launchLog = launchLog;
        _runOnUiThread = runOnUiThread;
        _closeTab = closeTab;
        _hostedService.OutputReceived += OnOutputReceived;
        _hostedService.Started += OnServiceStarted;
        _hostedService.Exited += OnServiceExited;
    }

    public HostedService HostedService => _hostedService;
    public override bool IsService => true;

    protected override void OnRunningStateChanged()
    {
        StopCommand.NotifyCanExecuteChanged();
        CloseCommand.NotifyCanExecuteChanged();
    }

    private bool CanStop() => IsRunning;

    private bool CanClose() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync() => await _hostedService.StopAsync();

    [RelayCommand]
    private async Task RestartAsync() => await _hostedService.RestartAsync();

    [RelayCommand(CanExecute = nameof(CanClose))]
    private void Close()
    {
        _hostedService.OutputReceived -= OnOutputReceived;
        _hostedService.Started -= OnServiceStarted;
        _hostedService.Exited -= OnServiceExited;
        _closeTab(this);
    }

    private void OnOutputReceived(ServiceOutputLine outputLine) => _runOnUiThread(() => AppendLine(outputLine.Text, outputLine.IsError, outputLine.Segments));

    private void OnServiceStarted() => _runOnUiThread(() =>
    {
        IsRunning = true;
        AppendLine($"▶ Démarrage : {_hostedService.Command.Executable} {string.Join(' ', _hostedService.Command.Arguments)}", false);
    });

    private void OnServiceExited(int exitCode, bool isStopRequested) => _runOnUiThread(() =>
    {
        IsRunning = false;
        if (isStopRequested)
        {
            AppendLine("⏹ Arrêté", false);
            return;
        }
        AppendLine($"💥 Arrêt inattendu (code {exitCode})", true);
        _launchLog.Error($"💥 {Title} s'est arrêté de lui-même (code {exitCode}) : voir son onglet");
    });
}
