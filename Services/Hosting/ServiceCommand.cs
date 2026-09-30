namespace DevLauncher.Services.Hosting;

/// <summary>Long-running service command, hosted either in a Windows Terminal tab or in a VSCode task.</summary>
/// <param name="ServiceProcessName">Process started by the command, used to detect that the service is running.</param>
public sealed record ServiceCommand(string Title, string WorkingDirectory, IReadOnlyList<string> PowerShellArguments, string ServiceProcessName);
