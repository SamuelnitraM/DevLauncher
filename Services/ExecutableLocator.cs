using System.IO;

namespace DevLauncher.Services;

/// <summary>
/// Resolves a command name (code, php, symfony…) the way the command prompt does : through the folders of the PATH
/// and the extensions of PATHEXT.
/// </summary>
public static class ExecutableLocator
{
    private const string DefaultExecutableExtensions = ".COM;.EXE;.BAT;.CMD";

    /// <summary>Returns the full path of the command found in the PATH of the current process, or null.</summary>
    public static string? FindInPath(string commandName)
        => FindInPath(commandName, Environment.GetEnvironmentVariable("PATH"), Environment.GetEnvironmentVariable("PATHEXT"));

    /// <summary>Returns the full path of the command found in the given PATH, trying the PATHEXT extensions when it has none.</summary>
    public static string? FindInPath(string commandName, string? pathVariable, string? pathExtensionsVariable)
    {
        if (string.IsNullOrWhiteSpace(commandName) || commandName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        var executableExtensions = (string.IsNullOrWhiteSpace(pathExtensionsVariable) ? DefaultExecutableExtensions : pathExtensionsVariable)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var candidateFileNames = Path.HasExtension(commandName)
            ? new[] { commandName }.Concat(executableExtensions.Select(extension => commandName + extension))
            : executableExtensions.Select(extension => commandName + extension).Append(commandName);
        var searchedFolders = (pathVariable ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(folder => folder.Trim('"'));
        foreach (var searchedFolder in searchedFolders)
        {
            foreach (var candidateFileName in candidateFileNames)
            {
                try
                {
                    var candidatePath = Path.Combine(searchedFolder, candidateFileName);
                    if (File.Exists(candidatePath)) return candidatePath;
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry is skipped.
                }
            }
        }
        return null;
    }

    /// <summary>Returns true for an existing file path, or for a bare command name found in the PATH.</summary>
    public static bool IsExecutableAvailable(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable)) return false;
        var trimmedExecutable = executable.Trim().Trim('"');
        return IsBareCommandName(trimmedExecutable) ? FindInPath(trimmedExecutable) is not null : File.Exists(trimmedExecutable);
    }

    /// <summary>A bare command name has no folder part : it is looked up in the PATH.</summary>
    public static bool IsBareCommandName(string executable)
        => executable.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, ':' }) < 0;
}
