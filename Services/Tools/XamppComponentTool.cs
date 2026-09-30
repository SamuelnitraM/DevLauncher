using DevLauncher.Models;

namespace DevLauncher.Services.Tools;

/// <summary>
/// XAMPP component shared by every project : started only when not already running,
/// stopped only when the launcher started it.
/// </summary>
public sealed class XamppComponentTool : LaunchTool
{
    public static readonly XamppComponentTool Apache = new(ToolIds.Apache, "Apache", "🌐", "httpd",
        () => AppSettings.ApacheExe, () => null, runsHidden: true);

    public static readonly XamppComponentTool MySql = new(ToolIds.MySql, "MySQL", "🗃️", "mysqld",
        () => AppSettings.MySQLExe, () => $"--defaults-file=\"{AppSettings.MySQLConfig}\"", runsHidden: true);

    public static readonly XamppComponentTool FileZilla = new(ToolIds.FileZilla, "FileZilla FTP", "📂", "FileZillaServer",
        () => AppSettings.FileZillaExe, () => "-compat -start", runsHidden: true);

    public static readonly XamppComponentTool Panel = new(ToolIds.XamppPanel, "Panneau XAMPP", "🖥️", "xampp-control",
        () => AppSettings.XamppPanel, () => null, runsHidden: false);

    private readonly string _processName;
    private readonly Func<string> _getExecutablePath;
    private readonly Func<string?> _getArguments;
    private readonly bool _runsHidden;

    private XamppComponentTool(string id, string displayName, string icon, string processName, Func<string> getExecutablePath, Func<string?> getArguments, bool runsHidden)
    {
        Id = id;
        DisplayName = displayName;
        Icon = icon;
        _processName = processName;
        _getExecutablePath = getExecutablePath;
        _getArguments = getArguments;
        _runsHidden = runsHidden;
    }

    public override string Id { get; }
    public override string DisplayName { get; }
    public override string Icon { get; }
    public override ToolCategory Category => ToolCategories.Xampp;
    public override LaunchStage Stage => LaunchStage.Infrastructure;
    public override ToolScope Scope => ToolScope.Machine;

    public override Task<ToolStartResult> StartAsync(ToolExecutionContext context)
    {
        if (ProcessHelper.IsProcessRunning(_processName))
        {
            context.Log.Info($"{Icon} {DisplayName} déjà en cours — ignoré");
            return Task.FromResult(ToolStartResult.AlreadyRunning);
        }
        context.Log.Info($"{Icon} Démarrage de {DisplayName}…");
        var isStarted = _runsHidden
            ? context.ProcessLauncher.StartHiddenProcess(_getExecutablePath(), _getArguments())
            : context.ProcessLauncher.StartShellProcess(_getExecutablePath(), _getArguments());
        return Task.FromResult(isStarted ? ToolStartResult.Started : ToolStartResult.Failed);
    }

    public override async Task StopAsync(ToolExecutionContext context)
    {
        context.Log.Info($"⏹ Arrêt de {DisplayName}…");
        await context.ProcessLauncher.StopProcessesAsync(DisplayName, process => process.ProcessName.Equals(_processName, StringComparison.OrdinalIgnoreCase));
    }
}
