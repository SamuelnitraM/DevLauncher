using System.IO;
using DevLauncher.Services.Assistants;
using Xunit;
using Xunit.Abstractions;

namespace DevLauncher.Tests;

public class InstalledApplicationLocatorTests
{
    private readonly ITestOutputHelper _testOutput;

    public InstalledApplicationLocatorTests(ITestOutputHelper testOutput)
    {
        _testOutput = testOutput;
    }

    [Fact]
    public void StartMenuApplicationsAreListed()
    {
        var startMenuApplications = InstalledApplicationLocator.ListStartMenuApplications();
        foreach (var (applicationName, applicationTarget) in startMenuApplications.Take(15)) _testOutput.WriteLine($"{applicationName} → {applicationTarget}");
        Assert.NotEmpty(startMenuApplications);
    }

    /// <summary>A shortcut added to the Start menu is found by its name, as a classic Claude installation would be.</summary>
    [Fact]
    public void ShortcutAddedToTheStartMenuIsFound()
    {
        var applicationName = $"DevLauncherTestApplication{Guid.NewGuid():N}";
        var startMenuProgramsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        var shortcutPath = Path.Combine(startMenuProgramsDirectory, applicationName + ".lnk");
        CreateShortcut(shortcutPath, Path.Combine(Environment.SystemDirectory, "notepad.exe"));
        try
        {
            InstalledApplicationLocator.ClearCache();
            var applicationTarget = InstalledApplicationLocator.FindStartMenuApplication(new[] { applicationName });
            _testOutput.WriteLine($"{applicationName} → {applicationTarget}");
            Assert.NotNull(applicationTarget);
            Assert.StartsWith(@"shell:AppsFolder\", applicationTarget);
        }
        finally
        {
            File.Delete(shortcutPath);
        }
    }

    [Fact]
    public void UnknownApplicationIsNotFound()
        => Assert.Null(InstalledApplicationLocator.FindStartMenuApplication(new[] { $"Missing{Guid.NewGuid():N}" }));

    /// <summary>Run only on a machine where Claude Desktop is installed (dedicated CI job).</summary>
    [Fact]
    [Trait("Category", "RealApplication")]
    public void InstalledClaudeDesktopIsDetected()
    {
        foreach (var (applicationName, applicationTarget) in InstalledApplicationLocator.ListStartMenuApplications().Where(application => application.Name.Contains("Claude", StringComparison.OrdinalIgnoreCase)))
            _testOutput.WriteLine($"Start menu : {applicationName} → {applicationTarget}");
        var detectedTarget = AssistantCatalog.DetectApplicationTarget(AssistantCatalog.GetDefinition(AssistantCatalog.ClaudeId));
        _testOutput.WriteLine($"Detected Claude target : {detectedTarget}");
        Assert.NotNull(detectedTarget);
    }

    private static void CreateShortcut(string shortcutPath, string targetPath)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell is not available");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetPath;
        shortcut.Save();
    }
}
