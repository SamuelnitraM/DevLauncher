using System.IO;

namespace DevLauncher.Services;

/// <summary>
/// Lit le contenu de htdocs et détecte si un projet est Symfony.
/// </summary>
public class ProjectScanner
{
    private readonly string _htdocsPath;

    public ProjectScanner(string htdocsPath)
    {
        _htdocsPath = htdocsPath;
    }

    /// <summary>
    /// Retourne la liste des chemins complets des sous-dossiers de htdocs,
    /// triés alphabétiquement.
    /// </summary>
    public List<string> GetProjects()
    {
        if (!Directory.Exists(_htdocsPath))
            return new List<string>();

        return Directory
            .GetDirectories(_htdocsPath)
            .OrderBy(Path.GetFileName)
            .ToList();
    }

    /// <summary>
    /// Détecte si un dossier contient un projet Symfony.
    /// Critères : présence de symfony.lock OU bin/console OU composer.json mentionnant symfony/framework-bundle.
    /// </summary>
    public bool IsSymfonyProject(string projectPath)
    {
        // Critère 1 : fichier symfony.lock
        if (File.Exists(Path.Combine(projectPath, "symfony.lock")))
            return true;

        // Critère 2 : bin/console (script de commande Symfony)
        if (File.Exists(Path.Combine(projectPath, "bin", "console")))
            return true;

        // Critère 3 : composer.json mentionne symfony/framework-bundle
        var composerJson = Path.Combine(projectPath, "composer.json");
        if (File.Exists(composerJson))
        {
            var content = File.ReadAllText(composerJson);
            if (content.Contains("symfony/framework-bundle", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
