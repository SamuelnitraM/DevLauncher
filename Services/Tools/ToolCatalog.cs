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
        XamppComponentTool.Apache,
        XamppComponentTool.MySql,
        XamppComponentTool.FileZilla,
        XamppComponentTool.Panel,
        new TerminalTool(),
        new BrowserTool(),
    };

    public IEnumerable<LaunchTool> GetCategoryTools(ToolCategory category) => Tools.Where(tool => tool.Category == category);
}
