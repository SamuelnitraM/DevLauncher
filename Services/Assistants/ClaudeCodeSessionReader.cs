using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DevLauncher.Services.Assistants;

/// <summary>Conversation of Claude Code saved for a project.</summary>
public sealed record ClaudeCodeSession(string SessionId, DateTime LastActivity, string Title);

/// <summary>
/// Reads the Claude Code sessions of a project from ~/.claude/projects, where each project folder
/// holds one JSON Lines file per session.
/// </summary>
public static partial class ClaudeCodeSessionReader
{
    private const int MaximumSessionCount = 15;
    private const int MaximumScannedLineCount = 80;
    private const int MaximumTitleLength = 70;

    private static string DefaultProjectsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    /// <summary>Returns the most recent sessions of the project, most recent first. Never throws.</summary>
    /// <param name="projectsDirectory">Folder of the Claude Code projects, ~/.claude/projects when null.</param>
    public static IReadOnlyList<ClaudeCodeSession> GetSessions(string projectPath, string? projectsDirectory = null)
    {
        try
        {
            var sessionDirectory = FindSessionDirectory(projectPath, projectsDirectory ?? DefaultProjectsDirectory);
            if (sessionDirectory is null) return Array.Empty<ClaudeCodeSession>();
            return new DirectoryInfo(sessionDirectory)
                .EnumerateFiles("*.jsonl")
                .OrderByDescending(sessionFile => sessionFile.LastWriteTime)
                .Take(MaximumSessionCount)
                .Select(sessionFile => new ClaudeCodeSession(
                    Path.GetFileNameWithoutExtension(sessionFile.Name),
                    sessionFile.LastWriteTime,
                    ReadSessionTitle(sessionFile.FullName)))
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<ClaudeCodeSession>();
        }
    }

    /// <summary>
    /// Claude Code names the folder of a project after its path, every character other than a letter or a digit
    /// becoming a dash. The comparison ignores the case, the drive letter case varying between tools.
    /// </summary>
    private static string? FindSessionDirectory(string projectPath, string projectsDirectory)
    {
        if (!Directory.Exists(projectsDirectory)) return null;
        var encodedProjectPath = EncodeProjectPath(projectPath);
        return Directory.EnumerateDirectories(projectsDirectory)
            .FirstOrDefault(directory => string.Equals(Path.GetFileName(directory), encodedProjectPath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Name of the folder in which Claude Code saves the sessions of a project.</summary>
    public static string EncodeProjectPath(string projectPath)
        => NonAlphanumericCharacterRegex().Replace(Path.TrimEndingDirectorySeparator(projectPath), "-");

    /// <summary>Uses the session summary when there is one, otherwise the first message typed by the user.</summary>
    private static string ReadSessionTitle(string sessionFilePath)
    {
        string? firstUserMessage = null;
        using var sessionReader = new StreamReader(new FileStream(sessionFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
        for (var lineIndex = 0; lineIndex < MaximumScannedLineCount && sessionReader.ReadLine() is { } sessionLine; lineIndex++)
        {
            try
            {
                using var sessionEntry = JsonDocument.Parse(sessionLine);
                var entryRoot = sessionEntry.RootElement;
                var entryType = entryRoot.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
                if (entryType == "summary" && entryRoot.TryGetProperty("summary", out var summaryElement) && summaryElement.GetString() is { Length: > 0 } summary)
                    return Shorten(summary);
                if (firstUserMessage is null && entryType == "user") firstUserMessage = ReadUserText(entryRoot);
            }
            catch (JsonException)
            {
                // A partially written line is skipped.
            }
        }
        return firstUserMessage is null ? "(session sans message)" : Shorten(firstUserMessage);
    }

    /// <summary>Returns the text typed by the user, ignoring the command outputs and system reminders injected in the session.</summary>
    private static string? ReadUserText(JsonElement entryRoot)
    {
        if (!entryRoot.TryGetProperty("message", out var messageElement) || !messageElement.TryGetProperty("content", out var contentElement)) return null;
        var userText = contentElement.ValueKind switch
        {
            JsonValueKind.String => contentElement.GetString(),
            JsonValueKind.Array => contentElement.EnumerateArray()
                .Where(part => part.TryGetProperty("type", out var partType) && partType.GetString() == "text")
                .Select(part => part.TryGetProperty("text", out var textElement) ? textElement.GetString() : null)
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(userText) || userText.TrimStart().StartsWith('<') ? null : userText;
    }

    private static string Shorten(string text)
    {
        var singleLineText = WhitespaceRegex().Replace(text, " ").Trim();
        return singleLineText.Length <= MaximumTitleLength ? singleLineText : singleLineText[..MaximumTitleLength] + "…";
    }

    [GeneratedRegex("[^a-zA-Z0-9]")]
    private static partial Regex NonAlphanumericCharacterRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
