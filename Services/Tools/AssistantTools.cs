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
        return Task.FromResult(context.ProcessLauncher.StartShellProcess(pageUrl, null, context.RunsUnelevated) ? ToolStartResult.Started : ToolStartResult.Failed);
    }

    /// <summary>Store applications are started through the shell namespace, the other targets directly.</summary>
    private static bool StartApplication(ToolExecutionContext context, string applicationTarget)
        => applicationTarget.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
            ? context.ProcessLauncher.StartShellProcess("explorer.exe", applicationTarget, context.RunsUnelevated)
            : context.ProcessLauncher.StartShellProcess(applicationTarget, null, context.RunsUnelevated);

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
/// Coding agent (Claude Code, Codex CLI…) opened in a terminal tab in the project folder, on a new conversation
/// or on the last one of the folder. Its command must be in the PATH : otherwise its installation page is given.
/// </summary>
public sealed class CliAgentTool : LaunchTool
{
    private const string NewSession = "new";
    private const string ContinueSession = "continue";

    private readonly CliAgentDefinition _agentDefinition;

    public CliAgentTool(CliAgentDefinition agentDefinition)
    {
        _agentDefinition = agentDefinition;
        Options = agentDefinition.ContinueArguments is null
            ? Array.Empty<ToolOptionDefinition>()
            : new[]
            {
                new ToolOptionDefinition(ToolIds.CliAgentSessionOption, "Conversation :", ToolOptionKind.SingleChoice,
                    _ => new[]
                    {
                        new ToolOptionChoice(NewSession, "✨ Nouvelle conversation"),
                        new ToolOptionChoice(ContinueSession, "↩️ Reprendre la dernière du dossier"),
                    },
                    _ => new[] { NewSession }),
            };
    }

    public override string Id => ToolIds.CliAgent(_agentDefinition.Id);
    public override string DisplayName => $"{_agentDefinition.DisplayName} (terminal)";
    public override string Icon => _agentDefinition.Icon;
    public override ToolCategory Category => ToolCategories.Assistants;
    public override LaunchStage Stage => LaunchStage.Workspace;
    public override IReadOnlyList<ToolOptionDefinition> Options { get; }
    public override bool IsEnabledInSettings => CliAgentCatalog.IsEnabled(_agentDefinition.Id);

    public override Task<ToolStartResult> StartAsync(ToolExecutionContext context)
    {
        if (CliAgentCatalog.FindInstalledCommand(_agentDefinition) is null)
        {
            context.Log.Error($"❌ {_agentDefinition.DisplayName} introuvable : la commande « {_agentDefinition.Command} » n'est pas dans le PATH — installation : {_agentDefinition.InstallUrl}");
            return Task.FromResult(ToolStartResult.Failed);
        }
        var isContinuing = context.GetOptionValues(ToolIds.CliAgentSessionOption).FirstOrDefault() == ContinueSession && _agentDefinition.ContinueArguments is not null;
        var agentArguments = isContinuing ? _agentDefinition.ContinueArguments! : Array.Empty<string>();
        context.Log.Info($"{Icon} Ouverture de {_agentDefinition.DisplayName} {(isContinuing ? "sur la dernière conversation" : "sur une nouvelle conversation")}…");
        // cmd /k keeps the tab open once the agent exits, so that its last messages stay readable.
        var commandLine = new[] { "cmd.exe", "/k", _agentDefinition.Command }.Concat(agentArguments).ToList();
        var isStarted = TerminalTabLauncher.OpenTab(context, $"{_agentDefinition.DisplayName} · {context.ProjectName}", commandLine);
        return Task.FromResult(isStarted ? ToolStartResult.Started : ToolStartResult.Failed);
    }
}
