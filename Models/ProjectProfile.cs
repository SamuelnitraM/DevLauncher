namespace DevLauncher.Models;

/// <summary>
/// Représente un préréglage sauvegardé pour un projet.
/// </summary>
public class ProjectProfile
{
    public string Name { get; set; } = "Défaut";

    // ── Type de projet ──────────────────────────────────
    public bool IsSymfony { get; set; }

    // ── Éditeur ─────────────────────────────────────────
    public bool OpenVSCode { get; set; }
    public bool OpenVisualStudio { get; set; }

    // ── Services XAMPP ──────────────────────────────────
    public bool ShowXamppPanel { get; set; }
    public bool StartApache { get; set; }
    public bool StartMySQL { get; set; }
    public bool StartFileZilla { get; set; }

    // ── Services Symfony ────────────────────────────────
    public bool StartSymfonyServer { get; set; }
    public bool StartTailwind { get; set; }
    public bool StartMercure { get; set; }
    public string? MercureScript { get; set; }

    // ── Outils ──────────────────────────────────────────
    public bool OpenTerminal { get; set; }
    public bool OpenBrowser { get; set; }
    public bool BrowserDefault { get; set; }
    public bool BrowserChrome { get; set; }
    public bool BrowserFirefox { get; set; }
}