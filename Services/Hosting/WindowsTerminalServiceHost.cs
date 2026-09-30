namespace DevLauncher.Services.Hosting;

/// <summary>Hosts the services in tabs of the DevLauncher Windows Terminal window.</summary>
public sealed class WindowsTerminalServiceHost
{
    private readonly ProcessLauncher _processLauncher;
    private readonly LaunchLog _launchLog;

    public WindowsTerminalServiceHost(ProcessLauncher processLauncher, LaunchLog launchLog)
    {
        _processLauncher = processLauncher;
        _launchLog = launchLog;
    }

    public void StartServices(IReadOnlyList<ServiceCommand> serviceCommands)
    {
        var windowsTerminalArguments = new List<string> { "-w", ProcessLauncher.WindowsTerminalWindowName };
        foreach (var serviceCommand in serviceCommands)
        {
            if (windowsTerminalArguments.Count > 2) windowsTerminalArguments.Add(";");
            windowsTerminalArguments.AddRange(new[]
            {
                "new-tab", "--title", serviceCommand.Title, "--suppressApplicationTitle",
                "-d", serviceCommand.WorkingDirectory, "powershell",
            });
            windowsTerminalArguments.AddRange(serviceCommand.PowerShellArguments);
            _launchLog.Info($"   → onglet {serviceCommand.Title}");
        }
        _processLauncher.StartWindowsTerminal(windowsTerminalArguments);
    }
}
