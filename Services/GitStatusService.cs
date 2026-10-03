using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace DevLauncher.Services;

/// <summary>State of the git repository of a project.</summary>
/// <param name="Branch">Current branch, or the short commit when the head is detached.</param>
/// <param name="ChangedFileCount">Modified, added, deleted and untracked files.</param>
/// <param name="AheadCount">Local commits not pushed, null without upstream branch or when unknown.</param>
/// <param name="BehindCount">Remote commits not pulled since the last fetch, null without upstream branch or when unknown.</param>
public sealed record GitStatus(string Branch, int? ChangedFileCount, int? AheadCount, int? BehindCount)
{
    /// <summary>Short summary shown under the project path.</summary>
    public string Summary
    {
        get
        {
            var summaryParts = new List<string> { $"🌿 {Branch}" };
            if (ChangedFileCount is { } changedFileCount)
                summaryParts.Add(changedFileCount == 0 ? "✔ à jour" : $"✏️ {changedFileCount} modification(s)");
            if (AheadCount > 0) summaryParts.Add($"⬆️ {AheadCount} à pousser");
            if (BehindCount > 0) summaryParts.Add($"⬇️ {BehindCount} en retard");
            return string.Join("  ·  ", summaryParts);
        }
    }
}

/// <summary>
/// Reads the git state of a project with « git status », without network access.
/// When git is not installed, the branch is read from .git/HEAD.
/// </summary>
public sealed class GitStatusService
{
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Returns the state of the repository, or null when the project is not a git repository.</summary>
    public async Task<GitStatus?> GetStatusAsync(string projectPath)
    {
        if (!IsGitRepository(projectPath)) return null;
        var statusOutput = await RunGitAsync(projectPath, StatusTimeout, "status", "--porcelain=v2", "--branch");
        return statusOutput is null ? ReadBranchFromHeadFile(projectPath) : ParsePorcelainStatus(statusOutput);
    }

    /// <summary>Downloads the remote state (git fetch) so that the commits behind are known. Returns false on failure.</summary>
    public async Task<bool> FetchAsync(string projectPath)
        => IsGitRepository(projectPath) && await RunGitAsync(projectPath, FetchTimeout, "fetch", "--quiet") is not null;

    /// <summary>A repository has a .git folder, or a .git file for a worktree or a submodule.</summary>
    public static bool IsGitRepository(string projectPath)
    {
        var gitPath = Path.Combine(projectPath, ".git");
        return Directory.Exists(gitPath) || File.Exists(gitPath);
    }

    /// <summary>Parses the output of « git status --porcelain=v2 --branch ».</summary>
    public static GitStatus ParsePorcelainStatus(string statusOutput)
    {
        string? branchName = null;
        string? commitIdentifier = null;
        int? aheadCount = null;
        int? behindCount = null;
        var changedFileCount = 0;
        foreach (var statusLine in statusOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (statusLine.StartsWith("# branch.head ", StringComparison.Ordinal))
            {
                branchName = statusLine["# branch.head ".Length..];
            }
            else if (statusLine.StartsWith("# branch.oid ", StringComparison.Ordinal))
            {
                commitIdentifier = statusLine["# branch.oid ".Length..];
            }
            else if (statusLine.StartsWith("# branch.ab ", StringComparison.Ordinal))
            {
                var aheadBehindParts = statusLine["# branch.ab ".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (aheadBehindParts.Length == 2 && int.TryParse(aheadBehindParts[0].TrimStart('+'), out var ahead) && int.TryParse(aheadBehindParts[1].TrimStart('-'), out var behind))
                {
                    aheadCount = ahead;
                    behindCount = behind;
                }
            }
            else if (!statusLine.StartsWith('#'))
            {
                changedFileCount++;
            }
        }
        var displayedBranch = branchName is null or "(detached)"
            ? $"détachée ({(commitIdentifier is { Length: >= 7 } ? commitIdentifier[..7] : "?")})"
            : branchName;
        return new GitStatus(displayedBranch, changedFileCount, aheadCount, behindCount);
    }

    /// <summary>Reads « ref: refs/heads/branch » from .git/HEAD. Used when git cannot be run.</summary>
    public static GitStatus? ReadBranchFromHeadFile(string projectPath)
    {
        try
        {
            var headFilePath = Path.Combine(projectPath, ".git", "HEAD");
            if (!File.Exists(headFilePath)) return null;
            var headContent = File.ReadAllText(headFilePath).Trim();
            const string branchReferencePrefix = "ref: refs/heads/";
            var branchName = headContent.StartsWith(branchReferencePrefix, StringComparison.Ordinal)
                ? headContent[branchReferencePrefix.Length..]
                : $"détachée ({(headContent.Length >= 7 ? headContent[..7] : headContent)})";
            return new GitStatus(branchName, null, null, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Runs git in the project folder and returns its output, or null when git is missing, fails or times out.</summary>
    private static async Task<string?> RunGitAsync(string projectPath, TimeSpan timeout, params string[] gitArguments)
    {
        var processStartInfo = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = projectPath,
        };
        foreach (var gitArgument in gitArguments) processStartInfo.ArgumentList.Add(gitArgument);
        // No credential prompt can appear in a hidden process : git fails instead of waiting forever.
        processStartInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        processStartInfo.Environment["GCM_INTERACTIVE"] = "never";
        try
        {
            using var gitProcess = Process.Start(processStartInfo);
            if (gitProcess is null) return null;
            using var timeoutCancellation = new CancellationTokenSource(timeout);
            var outputTask = gitProcess.StandardOutput.ReadToEndAsync(timeoutCancellation.Token);
            var errorTask = gitProcess.StandardError.ReadToEndAsync(timeoutCancellation.Token);
            try
            {
                await gitProcess.WaitForExitAsync(timeoutCancellation.Token);
                var gitOutput = await outputTask;
                await errorTask;
                return gitProcess.ExitCode == 0 ? gitOutput : null;
            }
            catch (OperationCanceledException)
            {
                gitProcess.Kill(entireProcessTree: true);
                return null;
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}
