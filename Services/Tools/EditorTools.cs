using System.IO;
using DevLauncher.Models;

namespace DevLauncher.Services.Tools;

/// <summary>Opens the project folder in VSCode. Can also host the services as VSCode tasks.</summary>
public sealed class VSCodeTool : LaunchTool
{
    public override string Id => ToolIds.VSCode;
    public override string DisplayName => "VSCode";
    public override string Icon => "💻";
    public override ToolCategory Category => ToolCategories.Editor;
    public override LaunchStage Stage => LaunchStage.Editor;

    public override Task<ToolStartResult> StartAsync(ToolExecutionContext context)
    {
        context.Log.Info("💻 Ouverture de VSCode…");
        return Task.FromResult(context.ProcessLauncher.StartVSCode(context.ProjectPath) ? ToolStartResult.Started : ToolStartResult.Failed);
    }

    public override Task StopAsync(ToolExecutionContext context)
    {
        context.ProcessLauncher.CloseApplicationWindows("VSCode", "Code", new[] { context.ProjectName });
        return Task.CompletedTask;
    }
}

/// <summary>Opens the solution of the project in Visual Studio, or the folder when there is no solution.</summary>
public sealed class VisualStudioTool : LaunchTool
{
    public override string Id => ToolIds.VisualStudio;
    public override string DisplayName => "Visual Studio";
    public override string Icon => "🟣";
    public override ToolCategory Category => ToolCategories.Editor;
    public override LaunchStage Stage => LaunchStage.Editor;

    public override Task<ToolStartResult> StartAsync(ToolExecutionContext context)
    {
        context.Log.Info("🟣 Ouverture de Visual Studio…");
        var solutionFilePath = FindSolutionFile(context.ProjectPath);
        context.Log.Info(solutionFilePath is not null
            ? $"   → Solution : {Path.GetFileName(solutionFilePath)}"
            : "   → Pas de solution, ouverture du dossier…");
        var isStarted = context.ProcessLauncher.StartShellProcess(AppSettings.VisualStudioExecutable, $"\"{solutionFilePath ?? context.ProjectPath}\"");
        return Task.FromResult(isStarted ? ToolStartResult.Started : ToolStartResult.Failed);
    }

    public override Task StopAsync(ToolExecutionContext context)
    {
        var solutionFilePath = FindSolutionFile(context.ProjectPath);
        var titleSegments = solutionFilePath is null
            ? new[] { context.ProjectName }
            : new[] { context.ProjectName, Path.GetFileNameWithoutExtension(solutionFilePath) };
        context.ProcessLauncher.CloseApplicationWindows("Visual Studio", "devenv", titleSegments);
        return Task.CompletedTask;
    }

    /// <summary>Returns the first solution file (.sln or .slnx) at the root of the project, if any.</summary>
    private static string? FindSolutionFile(string projectPath)
    {
        try
        {
            return Directory.EnumerateFiles(projectPath, "*.sln")
                .Concat(Directory.EnumerateFiles(projectPath, "*.slnx"))
                .FirstOrDefault();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
