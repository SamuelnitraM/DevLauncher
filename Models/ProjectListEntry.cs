namespace DevLauncher.Models;

/// <summary>Project displayed in the recent projects list or in the projects list.</summary>
/// <param name="Name">Folder name of the project.</param>
/// <param name="Path">Full path of the project folder.</param>
public record ProjectListEntry(string Name, string Path);
