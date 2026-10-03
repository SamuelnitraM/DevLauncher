using System.IO;

namespace DevLauncher.Services;

/// <summary>Compares folder paths the way Windows does : case-insensitive, trailing separator ignored.</summary>
public static class PathComparer
{
    public static bool AreSame(string firstPath, string secondPath)
        => string.Equals(Path.TrimEndingDirectorySeparator(firstPath), Path.TrimEndingDirectorySeparator(secondPath), StringComparison.OrdinalIgnoreCase);
}
