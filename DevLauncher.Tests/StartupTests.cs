using System.IO;
using System.Xml.Linq;
using DevLauncher.Services.Startup;
using Microsoft.Win32;
using Xunit;

namespace DevLauncher.Tests;

public class StartupCommandTests
{
    [Fact]
    public void CommandLineIsParsed()
    {
        Assert.Equal(new StartupCommand("templateSite", "Front", true, false), StartupCommand.Parse(new[] { "--project", "templateSite", "--profile", "Front" }));
        Assert.Equal(new StartupCommand(@"D:\dev\api", null, false, true), StartupCommand.Parse(new[] { "--minimized", "--open", "\"D:\\dev\\api\"" }));
        Assert.Equal(new StartupCommand(null, null, false, true), StartupCommand.Parse(new[] { "--minimized" }));
        Assert.Equal(new StartupCommand(null, null, false, false), StartupCommand.Parse(new[] { "--project", "--unknown" }));
        Assert.Equal(StartupCommand.Empty, StartupCommand.Parse(Array.Empty<string>()));
    }

    [Fact]
    public void LinksAreParsed()
    {
        Assert.Equal(new StartupCommand("templateSite", "Front Office", true, false, false), StartupCommand.Parse(new[] { "devlauncher://launch/templateSite?profile=Front%20Office" }));
        Assert.Equal(new StartupCommand("mon site", null, false, false, false), StartupCommand.Parse(new[] { "devlauncher://open/mon%20site/" }));
        Assert.Equal(StartupCommand.Empty, StartupCommand.Parse(new[] { "devlauncher://delete/templateSite" }));
        Assert.Equal(StartupCommand.Empty, StartupCommand.Parse(new[] { "devlauncher:" }));
    }

    [Fact]
    public void RequestedFolderIsNormalized()
    {
        Assert.Null(StartupCommand.NormalizeRequestedFolder("templateSite"));
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(@"C:\", StartupCommand.NormalizeRequestedFolder("C:"));
        Assert.Equal(@"C:\xampp\htdocs\site", StartupCommand.NormalizeRequestedFolder(@"C:\xampp\htdocs\site\"));
        Assert.Null(StartupCommand.NormalizeRequestedFolder(@"C:relatif"));
    }

    [Fact]
    public void LinkAndArgumentsRoundTrip()
    {
        var launchLink = StartupCommand.BuildLaunchLink("mon site", "Front & Back");
        Assert.Equal("devlauncher://launch/mon%20site?profile=Front%20%26%20Back", launchLink);
        var linkCommand = StartupCommand.Parse(new[] { launchLink });
        Assert.Equal(new StartupCommand("mon site", "Front & Back", true, false, false), linkCommand);
        Assert.Equal(linkCommand, StartupCommand.Parse(linkCommand.ToArguments()));
        var openCommand = new StartupCommand(@"C:\xampp\htdocs\site", null, false, true);
        Assert.Equal(openCommand, StartupCommand.Parse(openCommand.ToArguments()));
    }
}

public class SingleInstanceCoordinatorTests
{
    [Fact]
    public async Task SecondInstanceForwardsItsArguments()
    {
        if (!OperatingSystem.IsWindows()) return;
        var instanceName = $"DevLauncherTest-{Guid.NewGuid():N}";
        using var primaryCoordinator = new SingleInstanceCoordinator(instanceName);
        Assert.True(primaryCoordinator.TryBecomePrimaryInstance());
        var receivedArguments = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        primaryCoordinator.ArgumentsReceived += forwardedArguments => receivedArguments.TrySetResult(forwardedArguments);
        primaryCoordinator.StartListening();
        var secondaryTask = Task.Run(() =>
        {
            using var secondaryCoordinator = new SingleInstanceCoordinator(instanceName);
            return (IsPrimary: secondaryCoordinator.TryBecomePrimaryInstance(), IsForwarded: secondaryCoordinator.TryForwardToPrimaryInstance(new[] { "--project", "mon site" }));
        });
        var (isSecondaryPrimary, isForwarded) = await secondaryTask;
        Assert.False(isSecondaryPrimary);
        Assert.True(isForwarded);
        Assert.Equal(new[] { "--project", "mon site" }, await receivedArguments.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void ForwardingWithoutPrimaryInstanceFails()
        => Assert.False(new SingleInstanceCoordinator($"DevLauncherTest-{Guid.NewGuid():N}").TryForwardToPrimaryInstance(new[] { "--minimized" }));
}

public class WindowsIntegrationTests
{
    [Fact]
    public void CommandsQuoteTheExecutableAndTheFolder()
    {
        Assert.Equal("\"C:\\Outils\\DevLauncher.exe\" \"%1\"", WindowsIntegrationService.BuildProtocolCommand(@"C:\Outils\DevLauncher.exe"));
        Assert.Equal("\"C:\\Outils\\DevLauncher.exe\" --project \"%V\"", WindowsIntegrationService.BuildFolderCommand(@"C:\Outils\DevLauncher.exe", "%V"));
    }

    [Fact]
    public void StartupTaskDefinitionIsValidXml()
    {
        var taskDocument = XDocument.Parse(WindowsIntegrationService.BuildStartupTaskXml(@"C:\Outils & Co\DevLauncher.exe", @"POSTE\Développeur"));
        XNamespace taskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        Assert.Equal(@"C:\Outils & Co\DevLauncher.exe", taskDocument.Descendants(taskNamespace + "Command").Single().Value);
        Assert.Equal("--minimized", taskDocument.Descendants(taskNamespace + "Arguments").Single().Value);
        Assert.Equal("HighestAvailable", taskDocument.Descendants(taskNamespace + "RunLevel").Single().Value);
        Assert.Equal("false", taskDocument.Descendants(taskNamespace + "DisallowStartIfOnBatteries").Single().Value);
    }

    [Fact]
    public void ShellIntegrationIsRegisteredAndRemoved()
    {
        if (!OperatingSystem.IsWindows()) return;
        var testClassesKeyPath = $@"Software\DevLauncherTests\{Guid.NewGuid():N}\Classes";
        try
        {
            var windowsIntegrationService = new WindowsIntegrationService(Registry.CurrentUser, testClassesKeyPath, @"C:\Outils\DevLauncher.exe");
            Assert.False(windowsIntegrationService.IsShellIntegrationRegistered());
            windowsIntegrationService.RegisterShellIntegration();
            Assert.True(windowsIntegrationService.IsShellIntegrationRegistered());
            Assert.False(windowsIntegrationService.IsShellIntegrationOutdated());
            using (var folderCommandKey = Registry.CurrentUser.OpenSubKey($@"{testClassesKeyPath}\Directory\shell\DevLauncher\command"))
                Assert.Equal("\"C:\\Outils\\DevLauncher.exe\" --project \"%1\"", folderCommandKey?.GetValue(null));
            Assert.True(new WindowsIntegrationService(Registry.CurrentUser, testClassesKeyPath, @"D:\Nouveau\DevLauncher.exe").IsShellIntegrationOutdated());
            windowsIntegrationService.UnregisterShellIntegration();
            Assert.False(windowsIntegrationService.IsShellIntegrationRegistered());
            using var backgroundMenuKey = Registry.CurrentUser.OpenSubKey($@"{testClassesKeyPath}\Directory\Background\shell\DevLauncher");
            Assert.Null(backgroundMenuKey);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\DevLauncherTests", throwOnMissingSubKey: false);
        }
    }
}
