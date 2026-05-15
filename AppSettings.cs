namespace DevLauncher;

/// <summary>
/// Paramètres globaux de l'application.
/// Modifie ici les chemins si ton installation XAMPP est différente.
/// </summary>
public static class AppSettings
{
    /// <summary>Dossier des projets web XAMPP</summary>
    public static string HtdocsPath { get; set; } = @"C:\xampp\htdocs";

    /// <summary>Racine de l'installation XAMPP</summary>
    public static string XamppDir { get; set; } = @"C:\xampp";

    // Exécutables XAMPP directs
    public static string ApacheExe { get; set; } = @"C:\xampp\apache\bin\httpd.exe";
    public static string MySQLExe { get; set; } = @"C:\xampp\mysql\bin\mysqld.exe";
    public static string FileZillaExe { get; set; } = @"C:\xampp\FileZillaFTP\FileZillaServer.exe";
    public static string XamppPanel { get; set; } = @"C:\xampp\xampp-control.exe";

    // MySQL config
    public static string MySQLConfig { get; set; } = @"C:\xampp\mysql\bin\my.ini";

    // Navigateurs
    public static string ChromeExe { get; set; } = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
    public static string FirefoxExe { get; set; } = @"D:\Firefox\firefox.exe";

    /// <summary>Dossier contenant les scripts Mercure (start*.ps1)</summary>
    public static string MercureDir { get; set; } = @"C:\mercure";

    /// <summary>Chemin vers l'exécutable VSCode (doit être dans le PATH ou chemin absolu)</summary>
    public static string VSCodeExecutable { get; set; } = "code";

    /// <summary>Chemin vers l'exécutable Visual Studio</summary>
    public static string VisualStudioExecutable { get; set; } = @"C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\devenv.exe";

    /// <summary>Port local du serveur Symfony (pour l'ouverture navigateur)</summary>
    public static int SymfonyPort { get; set; } = 8000;

    /// <summary>Port local pour un projet PHP/HTML classique via Apache</summary>
    public static int LocalWebPort { get; set; } = 80;
}
