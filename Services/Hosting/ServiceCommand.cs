namespace DevLauncher.Services.Hosting;

/// <summary>Long-running service command, run by DevLauncher itself or as a VSCode task.</summary>
/// <param name="ToolId">Tool producing the command, identifying the service inside its project.</param>
/// <param name="ServiceProcessName">Process started by the command, used to detect that a VSCode task has started.</param>
/// <param name="AnnouncesApplicationUrl">The command is the web server of the project : the first local URL it prints is the project URL.</param>
/// <param name="EnvironmentVariables">Variables added to the environment of the command.</param>
public sealed record ServiceCommand(
    string ToolId,
    string Title,
    string WorkingDirectory,
    string Executable,
    IReadOnlyList<string> Arguments,
    string ServiceProcessName,
    bool AnnouncesApplicationUrl = false,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null);
