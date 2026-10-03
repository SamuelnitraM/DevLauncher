using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DevLauncher.Services.Stacks;

/// <summary>Virtual host of a project : its name (projet.test) and the folder Apache serves.</summary>
public sealed record VirtualHostEntry(string ProjectPath, string HostName, string DocumentRoot);

/// <summary>
/// Gives each project its own address (projet.test) : an Apache virtual host in a configuration file owned by DevLauncher,
/// included once in httpd.conf, and the matching lines of the Windows hosts file. The list of virtual hosts is kept
/// in the data directory ; the Apache file and the hosts lines are generated from it.
/// </summary>
public sealed partial class VirtualHostService
{
    public const string HostNameSuffix = ".test";
    private const string HostsLineMarker = "# DevLauncher";
    private const string IncludedFileName = "devlauncher-vhosts.conf";

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private readonly string _entriesFilePath;
    private readonly Func<string> _getXamppDirectory;
    private readonly string _hostsFilePath;

    /// <summary>Virtual hosts of the XAMPP of the settings (read at each use, so that a changed folder applies at once) and of the hosts file of Windows.</summary>
    public VirtualHostService() : this(
        StoragePaths.VirtualHostsFilePath,
        () => AppSettings.XamppDir,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts"))
    {
    }

    public VirtualHostService(string entriesFilePath, string xamppDirectory, string hostsFilePath) : this(entriesFilePath, () => xamppDirectory, hostsFilePath)
    {
    }

    private VirtualHostService(string entriesFilePath, Func<string> getXamppDirectory, string hostsFilePath)
    {
        _entriesFilePath = entriesFilePath;
        _getXamppDirectory = getXamppDirectory;
        _hostsFilePath = hostsFilePath;
    }

    private string ApacheConfigurationDirectory => Path.Combine(_getXamppDirectory(), "apache", "conf");
    private string IncludedConfigurationPath => Path.Combine(ApacheConfigurationDirectory, "extra", IncludedFileName);
    private string MainConfigurationPath => Path.Combine(ApacheConfigurationDirectory, "httpd.conf");

    /// <summary>Address of a project : its folder name in lower case, other characters than letters and digits turned into dashes.</summary>
    public static string BuildHostName(string projectPath)
    {
        var projectName = Path.GetFileName(Path.TrimEndingDirectorySeparator(projectPath)).ToLowerInvariant();
        var unaccentedProjectName = string.Concat(projectName.Normalize(NormalizationForm.FormD)
            .Where(character => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character) != System.Globalization.UnicodeCategory.NonSpacingMark));
        var hostLabel = HostLabelRegex().Replace(unaccentedProjectName, "-").Trim('-');
        return (hostLabel.Length == 0 ? "projet" : hostLabel) + HostNameSuffix;
    }

    /// <summary>Public folder of the frameworks serving from a sub-folder (Symfony, Laravel), the project folder otherwise.</summary>
    public static string FindDocumentRoot(string projectPath)
    {
        var publicFolderPath = Path.Combine(projectPath, "public");
        return Directory.Exists(publicFolderPath) ? publicFolderPath : projectPath;
    }

    /// <summary>Virtual hosts of the list, none when the list is missing or unreadable.</summary>
    public IReadOnlyList<VirtualHostEntry> GetEntries()
    {
        try
        {
            return ReadEntries();
        }
        catch (InvalidDataException)
        {
            return Array.Empty<VirtualHostEntry>();
        }
    }

    /// <summary>Reads the list before a change. An unreadable list throws : rewriting the files from it would drop every other virtual host.</summary>
    private List<VirtualHostEntry> ReadEntries()
    {
        if (!File.Exists(_entriesFilePath)) return new List<VirtualHostEntry>();
        try
        {
            return JsonSerializer.Deserialize<List<VirtualHostEntry>>(File.ReadAllText(_entriesFilePath)) ?? new List<VirtualHostEntry>();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"Liste des hôtes virtuels illisible ({_entriesFilePath}) : {exception.Message}", exception);
        }
    }

    /// <summary>Returns the address of the project when it has a virtual host, or null.</summary>
    public string? FindHostName(string projectPath)
        => GetEntries().FirstOrDefault(entry => PathComparer.AreSame(entry.ProjectPath, projectPath))?.HostName;

    /// <summary>Creates the virtual host of the project and returns its address. Throws IOException or UnauthorizedAccessException when a file cannot be written.</summary>
    public string Add(string projectPath, int apachePort)
    {
        var entries = ReadEntries().Where(entry => !PathComparer.AreSame(entry.ProjectPath, projectPath)).ToList();
        var hostName = BuildHostName(projectPath);
        // Two projects with the same folder name get distinct addresses.
        for (var suffixNumber = 2; entries.Any(entry => entry.HostName == hostName); suffixNumber++)
            hostName = $"{BuildHostName(projectPath)[..^HostNameSuffix.Length]}-{suffixNumber}{HostNameSuffix}";
        entries.Add(new VirtualHostEntry(projectPath, hostName, FindDocumentRoot(projectPath)));
        Apply(entries, apachePort);
        return hostName;
    }

    /// <summary>Removes the virtual host of the project.</summary>
    public void Remove(string projectPath, int apachePort)
        => Apply(ReadEntries().Where(entry => !PathComparer.AreSame(entry.ProjectPath, projectPath)).ToList(), apachePort);

    /// <summary>
    /// Writes the Apache file, its include in httpd.conf and the hosts lines, then the list : the list only records
    /// a virtual host once Apache and the hosts file know it.
    /// </summary>
    private void Apply(IReadOnlyList<VirtualHostEntry> entries, int apachePort)
    {
        if (!File.Exists(MainConfigurationPath)) throw new FileNotFoundException($"httpd.conf introuvable dans {ApacheConfigurationDirectory}", MainConfigurationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(IncludedConfigurationPath)!);
        File.WriteAllText(IncludedConfigurationPath, BuildApacheConfiguration(entries, _getXamppDirectory(), apachePort));
        EnsureIncludedInMainConfiguration();
        File.WriteAllLines(_hostsFilePath, BuildHostsLines(File.Exists(_hostsFilePath) ? File.ReadAllLines(_hostsFilePath) : Array.Empty<string>(), entries));
        Directory.CreateDirectory(Path.GetDirectoryName(_entriesFilePath)!);
        File.WriteAllText(_entriesFilePath, JsonSerializer.Serialize(entries, _jsonOptions));
    }

    /// <summary>
    /// The first virtual host answers the unknown names : it keeps localhost on the Apache folder, so that
    /// http://localhost/projet and phpMyAdmin still work once the project addresses exist.
    /// </summary>
    public static string BuildApacheConfiguration(IEnumerable<VirtualHostEntry> entries, string xamppDirectory, int apachePort)
    {
        static string ToApachePath(string path) => path.Replace('\\', '/');
        var configurationBuilder = new StringBuilder();
        configurationBuilder.AppendLine("# Virtual hosts of the projects, generated by DevLauncher : changes made here are overwritten.");
        configurationBuilder.AppendLine();
        configurationBuilder.AppendLine($"<VirtualHost *:{apachePort}>");
        configurationBuilder.AppendLine("    ServerName localhost");
        configurationBuilder.AppendLine($"    DocumentRoot \"{ToApachePath(Path.Combine(xamppDirectory, "htdocs"))}\"");
        configurationBuilder.AppendLine("</VirtualHost>");
        foreach (var entry in entries)
        {
            var documentRoot = ToApachePath(entry.DocumentRoot);
            configurationBuilder.AppendLine();
            configurationBuilder.AppendLine($"# {entry.ProjectPath}");
            configurationBuilder.AppendLine($"<VirtualHost *:{apachePort}>");
            configurationBuilder.AppendLine($"    ServerName {entry.HostName}");
            configurationBuilder.AppendLine($"    DocumentRoot \"{documentRoot}\"");
            configurationBuilder.AppendLine($"    <Directory \"{documentRoot}\">");
            configurationBuilder.AppendLine("        Options Indexes FollowSymLinks");
            configurationBuilder.AppendLine("        AllowOverride All");
            configurationBuilder.AppendLine("        Require all granted");
            configurationBuilder.AppendLine("    </Directory>");
            configurationBuilder.AppendLine("</VirtualHost>");
        }
        return configurationBuilder.ToString();
    }

    /// <summary>Replaces the lines written by DevLauncher in the hosts file, keeping every other line as is.</summary>
    public static IReadOnlyList<string> BuildHostsLines(IEnumerable<string> currentHostsLines, IEnumerable<VirtualHostEntry> entries)
    {
        var hostsLines = currentHostsLines.Where(hostsLine => !hostsLine.TrimEnd().EndsWith(HostsLineMarker, StringComparison.Ordinal)).ToList();
        foreach (var entry in entries)
        {
            hostsLines.Add($"127.0.0.1\t{entry.HostName}\t{HostsLineMarker}");
            hostsLines.Add($"::1\t{entry.HostName}\t{HostsLineMarker}");
        }
        return hostsLines;
    }

    private void EnsureIncludedInMainConfiguration()
    {
        var includeDirective = $"Include \"conf/extra/{IncludedFileName}\"";
        var mainConfigurationLines = File.ReadAllLines(MainConfigurationPath);
        if (mainConfigurationLines.Any(configurationLine => configurationLine.Trim() == includeDirective)) return;
        File.AppendAllLines(MainConfigurationPath, new[] { string.Empty, "# Virtual hosts of the projects managed by DevLauncher", includeDirective });
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex HostLabelRegex();
}
