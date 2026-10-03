using System.IO;
using System.Text;
using DevLauncher.Models;

namespace DevLauncher.Services;

/// <summary>
/// Builds a Markdown description of a project ready to paste into an AI conversation : stack, folder tree,
/// useful commands, ports, local URL and git state.
/// </summary>
public static class ProjectContextBuilder
{
    private const int MaximumTreeLineCount = 80;
    private const int MaximumTreeDepth = 2;

    /// <summary>Generated, dependency or tool folders left out of the tree.</summary>
    private static readonly HashSet<string> _ignoredFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".idea", ".vs", ".vscode", "node_modules", "vendor", "var", "bin", "obj", "dist", "build", "storage",
        "__pycache__", ".venv", "venv", "env", ".next", ".nuxt", "coverage", ".cache",
    };

    public sealed record ProjectContextInput(
        string ProjectPath,
        ProjectType ProjectType,
        bool UsesTailwindBundle,
        IReadOnlyList<string> NpmScripts,
        string? LocalUrl,
        GitStatus? GitStatus);

    public static string Build(ProjectContextInput contextInput)
    {
        var projectName = Path.GetFileName(Path.TrimEndingDirectorySeparator(contextInput.ProjectPath));
        var contextBuilder = new StringBuilder();
        contextBuilder.AppendLine($"# Projet {projectName}");
        contextBuilder.AppendLine();
        contextBuilder.AppendLine($"- Dossier : `{contextInput.ProjectPath}`");
        contextBuilder.AppendLine($"- Stack : {DescribeStack(contextInput)}");
        if (contextInput.LocalUrl is not null) contextBuilder.AppendLine($"- URL locale : {contextInput.LocalUrl}");
        if (contextInput.GitStatus is { } gitStatus) contextBuilder.AppendLine($"- Git : branche `{gitStatus.Branch}`{(gitStatus.ChangedFileCount is > 0 ? $", {gitStatus.ChangedFileCount} fichier(s) modifié(s)" : string.Empty)}");
        contextBuilder.AppendLine();
        var usefulCommands = GetUsefulCommands(contextInput).ToList();
        if (usefulCommands.Count > 0)
        {
            contextBuilder.AppendLine("## Commandes utiles");
            contextBuilder.AppendLine();
            foreach (var usefulCommand in usefulCommands) contextBuilder.AppendLine($"- `{usefulCommand}`");
            contextBuilder.AppendLine();
        }
        contextBuilder.AppendLine("## Arborescence");
        contextBuilder.AppendLine();
        contextBuilder.AppendLine("```");
        contextBuilder.AppendLine($"{projectName}/");
        var treeLines = new List<string>();
        AppendTree(contextInput.ProjectPath, string.Empty, 1, treeLines);
        foreach (var treeLine in treeLines.Take(MaximumTreeLineCount)) contextBuilder.AppendLine(treeLine);
        if (treeLines.Count > MaximumTreeLineCount) contextBuilder.AppendLine($"… ({treeLines.Count - MaximumTreeLineCount} entrée(s) de plus)");
        contextBuilder.AppendLine("```");
        return contextBuilder.ToString();
    }

    private static string DescribeStack(ProjectContextInput contextInput)
    {
        var stackParts = new List<string> { ProjectTypeLabels.GetName(contextInput.ProjectType) };
        if (contextInput.UsesTailwindBundle) stackParts.Add("Tailwind (symfonycasts/tailwind-bundle)");
        if (contextInput.NpmScripts.Count > 0 && contextInput.ProjectType != ProjectType.Node) stackParts.Add("npm");
        if (DockerComposeFileExists(contextInput.ProjectPath)) stackParts.Add("Docker Compose");
        return string.Join(", ", stackParts);
    }

    private static bool DockerComposeFileExists(string projectPath)
        => new[] { "compose.yaml", "compose.yml", "docker-compose.yaml", "docker-compose.yml" }.Any(fileName => File.Exists(Path.Combine(projectPath, fileName)));

    private static IEnumerable<string> GetUsefulCommands(ProjectContextInput contextInput)
    {
        switch (contextInput.ProjectType)
        {
            case ProjectType.Symfony:
                yield return $"symfony server:start --port={AppSettings.SymfonyPort}";
                yield return "php bin/console cache:clear";
                yield return "php bin/console doctrine:migrations:migrate";
                if (contextInput.UsesTailwindBundle) yield return "php bin/console tailwind:build --watch";
                break;
            case ProjectType.Laravel:
                yield return "php artisan serve";
                yield return "php artisan migrate";
                yield return "php artisan queue:work";
                break;
            case ProjectType.Django:
                yield return "python manage.py runserver";
                yield return "python manage.py migrate";
                break;
            case ProjectType.DotNet:
                yield return "dotnet watch run";
                yield return "dotnet test";
                break;
        }
        foreach (var npmScript in contextInput.NpmScripts) yield return $"npm run {npmScript}";
    }

    /// <summary>Lists the folders first then the files, sorted by name, down to the maximum depth.</summary>
    private static void AppendTree(string folderPath, string indentation, int depth, List<string> treeLines)
    {
        if (treeLines.Count > MaximumTreeLineCount) return;
        List<DirectoryInfo> subFolders;
        List<FileInfo> files;
        try
        {
            var folderInfo = new DirectoryInfo(folderPath);
            subFolders = folderInfo.EnumerateDirectories()
                .Where(subFolder => !_ignoredFolderNames.Contains(subFolder.Name) && !subFolder.Attributes.HasFlag(FileAttributes.Hidden))
                .OrderBy(subFolder => subFolder.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            files = folderInfo.EnumerateFiles()
                .Where(file => !file.Attributes.HasFlag(FileAttributes.Hidden))
                .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }
        foreach (var subFolder in subFolders)
        {
            treeLines.Add($"{indentation}├── {subFolder.Name}/");
            if (depth < MaximumTreeDepth) AppendTree(subFolder.FullName, indentation + "│   ", depth + 1, treeLines);
        }
        foreach (var file in files) treeLines.Add($"{indentation}├── {file.Name}");
    }
}
