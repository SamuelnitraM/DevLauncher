using DevLauncher.Models;
using DevLauncher.Services.Assistants;

namespace DevLauncher.Services.Tools;

/// <summary>
/// Conversation assistant (Claude, ChatGPT…) opened in the browser, on a new conversation or on a saved project,
/// or in its desktop application, which opens on a new conversation.
/// </summary>
public sealed class ChatAssistantTool : LaunchTool
{
    private const string NewConversationValue = "";

    private readonly AssistantDefinition _assistantDefinition;

    public ChatAssistantTool(AssistantDefinition assistantDefinition)
    {
        _assistantDefinition = assistantDefinition;
        Options = new[]
        {
            new ToolOptionDefinition(ToolIds.AssistantModeOption, "Ouverture :", ToolOptionKind.SingleChoice,
                _ => new[]
                {
                    new ToolOptionChoice(AssistantModes.Browser, "🌐 Navigateur"),
                    new ToolOptionChoice(AssistantModes.Application, "🖥️ Application (nouvelle discussion)"),
                },
                _ => new[] { Settings.DefaultMode }),
            new ToolOptionDefinition(ToolIds.AssistantProjectOption, "Projet (navigateur) :", ToolOptionKind.SingleChoice,
                _ => GetProjectChoices(),
                GetDefaultProject),
        };
    }

    public override string Id => ToolIds.ChatAssistant(_assistantDefinition.Id);
    public override string DisplayName => _assistantDefinition.DisplayName;
    public override string Icon => _assistantDefinition.Icon;
    public override ToolCategory Category => ToolCategories.Assistants;
    public override LaunchStage Stage => LaunchStage.Workspace;
    public override IReadOnlyList<ToolOptionDefinition> Options { get; }
    public override bool IsEnabledInSettings => Settings.IsEnabled;

    private AssistantSettings Settings => AssistantCatalog.GetSettings(_assistantDefinition.Id);

    public override Task<ToolStartResult> StartAsync(ToolExecutionContext context)
    {
        var openingMode = context.GetOptionValues(ToolIds.AssistantModeOption).FirstOrDefault() ?? Settings.DefaultMode;
        if (openingMode == AssistantModes.Application)
        {
            var applicationTarget = AssistantCatalog.ResolveApplicationTarget(_assistantDefinition.Id);
            if (applicationTarget is not null)
            {
                context.Log.Info($"{Icon} Ouverture de l'application {DisplayName}…");
                return Task.FromResult(StartApplication(context, applicationTarget) ? ToolStartResult.Started : ToolStartResult.Failed);
            }
            context.Log.Error($"⚠️ Application {DisplayName} introuvable (à renseigner dans les paramètres) : ouverture dans le navigateur");
        }
        var projectUrl = context.GetOptionValues(ToolIds.AssistantProjectOption).FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
        var pageUrl = projectUrl ?? Settings.WebUrl;
        var projectName = Settings.Projects.FirstOrDefault(project => project.Url == projectUrl)?.Name;
        context.Log.Info(projectName is null
            ? $"{Icon} Ouverture de {DisplayName} : nouvelle discussion"
            : $"{Icon} Ouverture de {DisplayName} : projet « {projectName} »");
        return Task.FromResult(context.ProcessLauncher.StartShellProcess(pageUrl) ? ToolStartResult.Started : ToolStartResult.Failed);
    }

    /// <summary>Store applications are started through the shell namespace, the other targets directly.</summary>
    private static bool StartApplication(ToolExecutionContext context, string applicationTarget)
        => applicationTarget.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
            ? context.ProcessLauncher.StartShellProcess("explorer.exe", applicationTarget)
            : context.ProcessLauncher.StartShellProcess(applicationTarget);

    private IReadOnlyList<ToolOptionChoice> GetProjectChoices()
        => new[] { new ToolOptionChoice(NewConversationValue, "✨ Nouvelle discussion") }
            .Concat(Settings.Projects.Select(project => new ToolOptionChoice(project.Url, $"📁 {project.Name}")))
            .ToList();

    /// <summary>Proposes the saved project named like the project folder, a new conversation otherwise.</summary>
    private IReadOnlyList<string> GetDefaultProject(ToolOptionContext optionContext)
    {
        var projectFolderName = optionContext.ProjectPath is null ? null : System.IO.Path.GetFileName(optionContext.ProjectPath);
        var matchingProject = Settings.Projects.FirstOrDefault(project => string.Equals(project.Name, projectFolderName, StringComparison.OrdinalIgnoreCase));
        return new[] { matchingProject?.Url ?? NewConversationValue };
    }
}

/// <summary>
/// Claude Code opened in a terminal tab in the project folder : new session, last session, or one of the saved sessions of the project.
/// </summary>
public sealed class ClaudeCodeTool : LaunchTool
{
    private static readonly AssistantDefinition _claudeCodeDefinition = AssistantCatalog.GetDefinition(AssistantCatalog.ClaudeCodeId);

    public override string Id => ToolIds.ClaudeCode;
    public override string DisplayName => _claudeCodeDefinition.DisplayName;
    public override string Icon => _claudeCodeDefinition.Icon;
    public override ToolCategory Category => ToolCategories.Assistants;
    public override LaunchStage Stage => LaunchStage.Workspace;
    public override bool IsEnabledInSettings => AssistantCatalog.GetSettings(AssistantCatalog.ClaudeCodeId).IsEnabled;

    public override IReadOnlyList<ToolOptionDefinition> Options { get; } = new[]
    {
        new ToolOptionDefinition(ToolIds.ClaudeCodeSessionOption, "Session :", ToolOptionKind.SingleChoice,
            GetSessionChoices,
            _ => new[] { ToolIds.NewClaudeCodeSession }),
    };

    public override Task<ToolStartResult> StartAsync(ToolExecutionContext context)
    {
        var sessionChoice = context.GetOptionValues(ToolIds.ClaudeCodeSessionOption).FirstOrDefault() ?? ToolIds.NewClaudeCodeSession;
        var claudeArguments = sessionChoice switch
        {
            ToolIds.NewClaudeCodeSession => Array.Empty<string>(),
            ToolIds.ContinueClaudeCodeSession => new[] { "--continue" },
            _ => new[] { "--resume", sessionChoice },
        };
        context.Log.Info(sessionChoice switch
        {
            ToolIds.NewClaudeCodeSession => "⌨️ Ouverture de Claude Code : nouvelle session",
            ToolIds.ContinueClaudeCodeSession => "⌨️ Ouverture de Claude Code : reprise de la dernière session",
            _ => $"⌨️ Ouverture de Claude Code : reprise de la session {sessionChoice}",
        });
        // cmd resolves claude.cmd (npm install) as well as claude.exe (native install).
        var windowsTerminalArguments = new List<string>
        {
            "-w", ProcessLauncher.WindowsTerminalWindowName, "new-tab", "--title", $"Claude Code · {context.ProjectName}",
            "--suppressApplicationTitle", "-d", context.ProjectPath, "cmd", "/k", "claude",
        };
        windowsTerminalArguments.AddRange(claudeArguments);
        return Task.FromResult(context.ProcessLauncher.StartWindowsTerminal(windowsTerminalArguments) ? ToolStartResult.Started : ToolStartResult.Failed);
    }

    private static IReadOnlyList<ToolOptionChoice> GetSessionChoices(ToolOptionContext optionContext)
    {
        var sessionChoices = new List<ToolOptionChoice>
        {
            new(ToolIds.NewClaudeCodeSession, "✨ Nouvelle session"),
            new(ToolIds.ContinueClaudeCodeSession, "↩️ Continuer la dernière session"),
        };
        if (optionContext.ProjectPath is null) return sessionChoices;
        sessionChoices.AddRange(ClaudeCodeSessionReader.GetSessions(optionContext.ProjectPath)
            .Select(session => new ToolOptionChoice(session.SessionId, $"🕘 {session.LastActivity:dd/MM HH:mm} — {session.Title}")));
        return sessionChoices;
    }
}
