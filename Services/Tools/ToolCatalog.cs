using DevLauncher.Services.Assistants;

namespace DevLauncher.Services.Tools;

/// <summary>All the tools known by the launcher, in display order inside their category.</summary>
public sealed class ToolCatalog
{
    public ToolCatalog() : this(CreateDefaultTools())
    {
    }

    /// <summary>Catalog limited to the given tools.</summary>
    public ToolCatalog(IEnumerable<LaunchTool> tools)
    {
        Tools = tools.ToList();
    }

    public IReadOnlyList<LaunchTool> Tools { get; }

    private static IEnumerable<LaunchTool> CreateDefaultTools() => new LaunchTool[]
        {
            new VSCodeTool(),
            new VisualStudioTool(),
            new SymfonyServerTool(),
            new TailwindTool(),
            new MercureTool(),
            new MessengerWorkerTool(),
            new MailpitTool(),
            new DockerComposeTool(),
            new LaravelServerTool(),
            new LaravelQueueTool(),
            new NpmScriptTool(),
            new DjangoServerTool(),
            new DotNetWatchTool(),
            XamppComponentTool.Apache,
            XamppComponentTool.MySql,
            XamppComponentTool.FileZilla,
            XamppComponentTool.Panel,
        }
        .Concat(AssistantCatalog.Definitions.Select(assistantDefinition => new ChatAssistantTool(assistantDefinition)))
        .Concat(new LaunchTool[]
        {
            new DatabaseTool(),
            new PreLaunchCommandsTool(),
            new TerminalTool(),
            new BrowserTool(),
        });

    public IEnumerable<LaunchTool> GetCategoryTools(ToolCategory category) => Tools.Where(tool => tool.Category == category);
}
