namespace DevLauncher.Models;

/// <summary>Project displayed in the projects list, grouped between recent projects and all projects.</summary>
/// <param name="Name">Folder name of the project.</param>
/// <param name="Path">Full path of the project folder.</param>
/// <param name="GroupName">Header of the list section the project belongs to.</param>
public record ProjectListEntry(string Name, string Path, string GroupName);
