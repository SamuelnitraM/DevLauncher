namespace DevLauncher.Services.Startup;

/// <summary>
/// Request received at startup or forwarded by a second instance : a project to select, a profile, launch or not.
/// </summary>
/// <param name="Project">Folder name or full path of the project, null when no project is requested.</param>
/// <param name="ProfileName">Profile to use, null for the last used profile of the project.</param>
/// <param name="ShouldLaunch">Launches the project after selecting it.</param>
/// <param name="StartsMinimized">Starts without showing the main window.</param>
public sealed record StartupCommand(string? Project, string? ProfileName, bool ShouldLaunch, bool StartsMinimized)
{
    public const string ProtocolScheme = "devlauncher";

    public static StartupCommand Empty { get; } = new(null, null, false, false);

    public bool HasProject => !string.IsNullOrWhiteSpace(Project);

    /// <summary>
    /// Reads the command line : « --project name|path » (launches), « --open name|path » (selects only), « --profile name »,
    /// « --minimized », or a single « devlauncher://launch/name?profile=Front » or « devlauncher://open/name » link.
    /// Unknown arguments are ignored.
    /// </summary>
    public static StartupCommand Parse(IReadOnlyList<string> arguments)
    {
        if (arguments.Count > 0 && arguments[0].StartsWith(ProtocolScheme + ":", StringComparison.OrdinalIgnoreCase)) return ParseLink(arguments[0]);
        string? project = null;
        string? profileName = null;
        var shouldLaunch = false;
        var startsMinimized = false;
        for (var argumentIndex = 0; argumentIndex < arguments.Count; argumentIndex++)
        {
            var argument = arguments[argumentIndex];
            string? ReadValue() => argumentIndex + 1 < arguments.Count && !arguments[argumentIndex + 1].StartsWith("--", StringComparison.Ordinal) ? arguments[++argumentIndex] : null;
            switch (argument.ToLowerInvariant())
            {
                case "--project":
                case "--path":
                    project = ReadValue() ?? project;
                    shouldLaunch = true;
                    break;
                case "--open":
                    project = ReadValue() ?? project;
                    shouldLaunch = false;
                    break;
                case "--profile":
                    profileName = ReadValue() ?? profileName;
                    break;
                case "--minimized":
                    startsMinimized = true;
                    break;
            }
        }
        return new StartupCommand(Clean(project), Clean(profileName), shouldLaunch && project is not null, startsMinimized);
    }

    /// <summary>Reads « devlauncher://action/project?profile=name », the project being URL-encoded.</summary>
    public static StartupCommand ParseLink(string link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var linkUri) || !linkUri.Scheme.Equals(ProtocolScheme, StringComparison.OrdinalIgnoreCase)) return Empty;
        var action = linkUri.Host.ToLowerInvariant();
        if (action is not ("launch" or "open")) return Empty;
        var project = Uri.UnescapeDataString(linkUri.AbsolutePath.Trim('/'));
        var profileName = linkUri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(queryParameter => queryParameter.Split('=', 2))
            .Where(queryParts => queryParts.Length == 2 && queryParts[0].Equals("profile", StringComparison.OrdinalIgnoreCase))
            .Select(queryParts => Uri.UnescapeDataString(queryParts[1].Replace('+', ' ')))
            .FirstOrDefault();
        var cleanProject = Clean(project);
        return new StartupCommand(cleanProject, Clean(profileName), action == "launch" && cleanProject is not null, false);
    }

    /// <summary>Builds the link launching a project, for a README, a bookmark or a note.</summary>
    public static string BuildLaunchLink(string projectName, string? profileName)
        => $"{ProtocolScheme}://launch/{Uri.EscapeDataString(projectName)}{(profileName is null ? string.Empty : $"?profile={Uri.EscapeDataString(profileName)}")}";

    /// <summary>Arguments forwarded to the running instance : the same command written back as a command line.</summary>
    public IReadOnlyList<string> ToArguments()
    {
        var arguments = new List<string>();
        if (Project is not null) arguments.AddRange(new[] { ShouldLaunch ? "--project" : "--open", Project });
        if (ProfileName is not null) arguments.AddRange(new[] { "--profile", ProfileName });
        if (StartsMinimized) arguments.Add("--minimized");
        return arguments;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Trim('"');
}
