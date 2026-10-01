using System.IO;
using DevLauncher.Services.Assistants;
using Xunit;

namespace DevLauncher.Tests;

public class ClaudeCodeSessionReaderTests
{
    [Fact]
    public void ProjectPathIsEncodedLikeClaudeCode()
    {
        Assert.Equal("C--Users-sam-DevLauncher", ClaudeCodeSessionReader.EncodeProjectPath(@"C:\Users\sam\DevLauncher"));
        Assert.Equal("C--xampp-htdocs-highlight-forge", ClaudeCodeSessionReader.EncodeProjectPath(@"C:\xampp\htdocs\highlight.forge\"));
    }

    [Fact]
    public void SessionsAreListedMostRecentFirstWithReadableTitles()
    {
        using var claudeProjectsDirectory = new TemporaryDirectory();
        var projectPath = @"C:\xampp\htdocs\highlightforge";
        // The drive letter case differs between tools : the folder lookup ignores the case.
        var sessionDirectory = claudeProjectsDirectory.Combine("c--xampp-htdocs-highlightforge");
        Directory.CreateDirectory(sessionDirectory);
        var olderSessionPath = Path.Combine(sessionDirectory, "older-session.jsonl");
        File.WriteAllLines(olderSessionPath, new[]
        {
            """{"type":"user","message":{"role":"user","content":"<command-name>/init</command-name>"}}""",
            """{"type":"user","message":{"role":"user","content":[{"type":"text","text":"Corrige le formulaire\n de connexion"}]}}""",
            "{ partially written line",
        });
        File.SetLastWriteTime(olderSessionPath, DateTime.Now.AddHours(-3));
        File.WriteAllLines(Path.Combine(sessionDirectory, "newer-session.jsonl"), new[]
        {
            """{"type":"summary","summary":"Refonte de la galerie photo"}""",
            """{"type":"user","message":{"content":"autre chose"}}""",
        });
        var sessions = ClaudeCodeSessionReader.GetSessions(projectPath, claudeProjectsDirectory.DirectoryPath);
        Assert.Equal(new[] { "newer-session", "older-session" }, sessions.Select(session => session.SessionId));
        Assert.Equal("Refonte de la galerie photo", sessions[0].Title);
        Assert.Equal("Corrige le formulaire de connexion", sessions[1].Title);
    }

    [Fact]
    public void ProjectWithoutSessionGivesEmptyList()
    {
        using var claudeProjectsDirectory = new TemporaryDirectory();
        Assert.Empty(ClaudeCodeSessionReader.GetSessions(@"C:\nowhere", claudeProjectsDirectory.DirectoryPath));
        Assert.Empty(ClaudeCodeSessionReader.GetSessions(@"C:\nowhere", claudeProjectsDirectory.Combine("missing")));
    }
}
