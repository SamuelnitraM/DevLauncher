namespace DevLauncher.Models;

/// <summary>
/// Saved launch preset for a project. Also used as the launch request passed to the launch service.
/// </summary>
public class ProjectProfile
{
    public const string DefaultProfileName = "Défaut";

    public string Name { get; set; } = DefaultProfileName;

    // ── Project type ────────────────────────────────────
    public bool IsSymfony { get; set; }

    // ── Editor ──────────────────────────────────────────
    public bool OpenVSCode { get; set; }
    public bool OpenVisualStudio { get; set; }

    // ── XAMPP services ──────────────────────────────────
    public bool ShowXamppPanel { get; set; }
    public bool StartApache { get; set; }
    public bool StartMySQL { get; set; }
    public bool StartFileZilla { get; set; }

    // ── Symfony services ────────────────────────────────
    public bool StartSymfonyServer { get; set; }
    public bool StartTailwind { get; set; }
    public bool StartMercure { get; set; }
    public string? MercureScript { get; set; }

    // ── Tools ───────────────────────────────────────────
    public bool OpenTerminal { get; set; }
    public bool OpenBrowser { get; set; }
    public bool BrowserDefault { get; set; }
    public bool BrowserChrome { get; set; }
    public bool BrowserFirefox { get; set; }

    /// <summary>Builds the initial profile of a project from what was detected in its folder.</summary>
    public static ProjectProfile CreateDefault(ProjectDetection detection) => new()
    {
        Name = DefaultProfileName,
        IsSymfony = detection.IsSymfony,
        OpenVSCode = true,
        StartApache = !detection.IsSymfony,
        StartMySQL = true,
        StartSymfonyServer = detection.IsSymfony,
        StartTailwind = detection.IsSymfony && detection.UsesTailwindBundle,
        OpenBrowser = true,
        BrowserDefault = true,
    };
}
