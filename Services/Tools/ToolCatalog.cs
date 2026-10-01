using DevLauncher.Services.Assistants;

namespace DevLauncher.Services.Tools;

/// <summary>All the tools known by the launcher, in display order inside their category.</summary>
public sealed class ToolCatalog
{
    public IReadOnlyList<LaunchTool> Tools { get; } = new LaunchTool[]
        {
            new VSCodeTool(),
            new VisualStudioTool(),
            new SymfonyServerTool(),
            new TailwindTool(),
            new MercureTool(),
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
            new TerminalTool(),
            new BrowserTool(),
        })
        .ToList();

    public IEnumerable<LaunchTool> GetCategoryTools(ToolCategory category) => Tools.Where(tool => tool.Category == category);
}
