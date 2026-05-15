namespace DevLauncher.Services;

/// <summary>
/// Toutes les options cochées par l'utilisateur avant de lancer.
/// Ce simple record est transmis au LaunchService.
/// </summary>
public record LaunchOptions
{
    // ── Type de projet ──────────────────────────────────────
    public bool IsSymfony { get; init; }

    // ── Outils ──────────────────────────────────────────────
    public bool OpenVSCode          { get; init; }
    public bool OpenVisualStudio    { get; init; }
    public bool OpenBrowser { get; init; }
    public bool BrowserDefault { get; init; }
    public bool BrowserChrome { get; init; }
    public bool BrowserFirefox { get; init; }
    public bool OpenTerminal        { get; init; }  // ← NOUVEAU : terminal optionnel non-Symfony

    // ── Services XAMPP ──────────────────────────────────────
    public bool StartApache    { get; init; }
    public bool StartMySQL     { get; init; }
    public bool StartFileZilla { get; init; }
    public bool ShowXamppPanel { get; init; }

    // ── Services Symfony ────────────────────────────────────
    public bool StartSymfonyServer { get; init; }
    public bool StartTailwind      { get; init; }
    public bool StartMercure       { get; init; }
    public string? MercureScript   { get; init; }
}
