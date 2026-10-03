namespace DevLauncher.Services.Mcp;

/// <summary>Project as described to an AI : name, folder, type, profiles.</summary>
public sealed record AutomationProject(string Name, string Path, string ProjectType, IReadOnlyList<string> Profiles, bool IsFavorite);

/// <summary>Service as described to an AI : XAMPP component or service run by DevLauncher.</summary>
public sealed record AutomationService(string Name, string? Project, bool IsRunning, bool? IsReady);

/// <summary>Actions of DevLauncher an AI can drive through the MCP server. Every method runs the same code as the interface.</summary>
public interface IDevLauncherAutomation
{
    Task<IReadOnlyList<AutomationProject>> ListProjectsAsync();

    /// <summary>Launches a project (folder name or path) with a profile, the last used one when null. Returns the outcome.</summary>
    Task<string> LaunchProjectAsync(string project, string? profileName);

    /// <summary>Stops what DevLauncher launched, without asking. Returns the outcome.</summary>
    Task<string> StopAllAsync();

    Task<IReadOnlyList<AutomationService>> GetServiceStatusAsync();

    /// <summary>Returns the last lines of a service log (or of the launch log), null when no log matches the name.</summary>
    Task<IReadOnlyList<string>?> ReadServiceLogsAsync(string serviceName, int lineCount);

    /// <summary>Restarts a service run by DevLauncher. Returns the outcome.</summary>
    Task<string> RestartServiceAsync(string serviceName);
}
