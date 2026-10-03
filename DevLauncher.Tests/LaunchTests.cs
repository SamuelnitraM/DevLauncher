using System.IO;
using System.Net;
using System.Net.Sockets;
using DevLauncher.Models;
using DevLauncher.Services;
using DevLauncher.Services.Hosting;
using DevLauncher.Services.Tools;
using DevLauncher.ViewModels;
using Xunit;

namespace DevLauncher.Tests;

public class ConfigurationReaderTests
{
    [Fact]
    public void MySqlPortIsReadFromTheServerSection()
    {
        Assert.Equal(3307, MySqlConfigurationReader.ParseServerPort(new[] { "[client]", "port=3399", "[mysqld]", "# port=1", "port = 3307" }));
        Assert.Equal(MySqlConfigurationReader.DefaultPort, MySqlConfigurationReader.ParseServerPort(new[] { "[client]", "port=3399" }));
        Assert.Equal(MySqlConfigurationReader.DefaultPort, MySqlConfigurationReader.ReadServerPort("missing-my.ini"));
    }

    [Fact]
    public void ApacheListenDirectivesAreRead()
    {
        var listenPorts = ApacheConfigurationReader.ParseListenPorts(new[] { "#Listen 12.34.56.78:80", "Listen 80", "  Listen 0.0.0.0:8080", "Listen [::]:443 https", "Listen 80" });
        Assert.Equal(new[] { 80, 8080, 443 }, listenPorts);
        using var temporaryDirectory = new TemporaryDirectory();
        Assert.Equal(new[] { 8081 }, ApacheConfigurationReader.ReadListenPorts(temporaryDirectory.DirectoryPath, 8081));
    }

    [Fact]
    public void ApacheSslPortIsReadWhenTheSslFileIsIncluded()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        Directory.CreateDirectory(temporaryDirectory.Combine("apache", "conf", "extra"));
        File.WriteAllLines(temporaryDirectory.Combine("apache", "conf", "httpd.conf"), new[] { "Listen 80", "Include conf/extra/httpd-ssl.conf" });
        File.WriteAllLines(temporaryDirectory.Combine("apache", "conf", "extra", "httpd-ssl.conf"), new[] { "Listen 443" });
        Assert.Equal(new[] { 80, 443 }, ApacheConfigurationReader.ReadListenPorts(temporaryDirectory.DirectoryPath, 80));
    }
}

public class PortOwnerLocatorTests
{
    [Fact]
    public void NetworkOrderPortIsConverted()
    {
        Assert.Equal(80, PortOwnerLocator.ConvertNetworkOrderPort(0x5000));
        Assert.Equal(8000, PortOwnerLocator.ConvertNetworkOrderPort(0x401F));
    }

    [Fact]
    public void ListeningProcessIsFound()
    {
        if (!OperatingSystem.IsWindows()) return;
        var tcpListener = new TcpListener(IPAddress.Loopback, 0);
        tcpListener.Start();
        try
        {
            var listenedPort = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
            Assert.Equal(Environment.ProcessId, PortOwnerLocator.FindListeningProcessId(listenedPort));
        }
        finally
        {
            tcpListener.Stop();
        }
    }
}

public class LaunchServiceTests
{
    /// <summary>Tool counting its starts, listening nowhere but requiring a given port.</summary>
    private sealed class PortRequiringTool : LaunchTool
    {
        private readonly int _requiredPort;

        public PortRequiringTool(int requiredPort, string id, LaunchStage stage)
        {
            _requiredPort = requiredPort;
            Id = id;
            Stage = stage;
        }

        public int StartCount { get; private set; }
        public override string Id { get; }
        public override string DisplayName => $"Outil {Id}";
        public override string Icon => "🧪";
        public override ToolCategory Category => ToolCategories.Utilities;
        public override LaunchStage Stage { get; }

        public override IReadOnlyList<RequiredPort> GetRequiredPorts(ToolExecutionContext context)
            => _requiredPort == 0 ? Array.Empty<RequiredPort>() : new[] { new RequiredPort(_requiredPort, new[] { "processus-accepte" }) };

        public override Task<ToolStartResult> StartAsync(ToolExecutionContext context)
        {
            StartCount++;
            return Task.FromResult(ToolStartResult.Started);
        }
    }

    private sealed class RecordingObserver : ILaunchObserver
    {
        private readonly PortConflictDecision _decision;

        public RecordingObserver(PortConflictDecision decision)
        {
            _decision = decision;
        }

        public List<LaunchProgress> ProgressReports { get; } = new();
        public List<PortConflict> PortConflicts { get; } = new();

        public void ReportProgress(LaunchProgress progress) => ProgressReports.Add(progress);

        public PortConflictDecision ResolvePortConflict(PortConflict portConflict)
        {
            PortConflicts.Add(portConflict);
            return _decision;
        }
    }

    private static LaunchService CreateLaunchService(params LaunchTool[] tools)
    {
        var launchLog = new LaunchLog();
        return new LaunchService(new ToolCatalog(tools), new ProcessLauncher(launchLog), new VSCodeTasksServiceHost(new ProcessEventWatcher(), launchLog), new ServiceProcessHost(launchLog), launchLog);
    }

    private static ProjectProfile CreateProfile(params string[] enabledToolIds)
    {
        var profile = new ProjectProfile();
        foreach (var toolId in enabledToolIds) profile.Tools[toolId] = ToolSelection.Enabled();
        return profile;
    }

    [Fact]
    public async Task ProgressCoversEveryStepInStageOrder()
    {
        var finalTool = new PortRequiringTool(0, "final", LaunchStage.Finalization);
        var editorTool = new PortRequiringTool(0, "editor", LaunchStage.Editor);
        var unusedTool = new PortRequiringTool(0, "unused", LaunchStage.Workspace);
        var launchObserver = new RecordingObserver(PortConflictDecision.Ignore);
        Assert.True(await CreateLaunchService(finalTool, editorTool, unusedTool).LaunchAsync("/projet", CreateProfile("final", "editor"), launchObserver));
        Assert.Equal(1, finalTool.StartCount);
        Assert.Equal(1, editorTool.StartCount);
        Assert.Equal(0, unusedTool.StartCount);
        Assert.Equal(new[] { "🔌 Vérification des ports", "🧪 Outil editor", "🧪 Outil final", "✅ Terminé" }, launchObserver.ProgressReports.Select(progress => progress.StepLabel));
        Assert.All(launchObserver.ProgressReports, progress => Assert.Equal(3, progress.TotalStepCount));
        Assert.Equal(100, launchObserver.ProgressReports[^1].Percentage);
    }

    [Theory]
    [InlineData(PortConflictDecision.CancelLaunch, false, 0)]
    [InlineData(PortConflictDecision.Ignore, true, 1)]
    public async Task PortHeldByAnotherProgramIsReported(PortConflictDecision decision, bool isLaunchExpected, int expectedStartCount)
    {
        if (!OperatingSystem.IsWindows()) return;
        var tcpListener = new TcpListener(IPAddress.Loopback, 0);
        tcpListener.Start();
        try
        {
            var heldPort = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
            var portTool = new PortRequiringTool(heldPort, "port", LaunchStage.Infrastructure);
            var launchObserver = new RecordingObserver(decision);
            Assert.Equal(isLaunchExpected, await CreateLaunchService(portTool).LaunchAsync("C:\\projet", CreateProfile("port"), launchObserver));
            var portConflict = Assert.Single(launchObserver.PortConflicts);
            Assert.Equal(heldPort, portConflict.Port);
            Assert.Equal(Environment.ProcessId, portConflict.OwnerProcessId);
            Assert.Equal(expectedStartCount, portTool.StartCount);
        }
        finally
        {
            tcpListener.Stop();
        }
    }
}

public class LaunchStatisticsServiceTests
{
    [Fact]
    public void LaunchesAreCountedAndTimed()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var statisticsService = new LaunchStatisticsService(temporaryDirectory.Combine("launch-statistics.json"));
        statisticsService.RecordLaunch(@"C:\xampp\htdocs\site", new DateTime(2026, 10, 1, 9, 0, 0), TimeSpan.FromSeconds(10));
        statisticsService.RecordLaunch(@"C:\xampp\htdocs\api", new DateTime(2026, 10, 2, 9, 0, 0), TimeSpan.FromSeconds(4));
        statisticsService.RecordLaunch(@"C:\XAMPP\htdocs\SITE", new DateTime(2026, 10, 3, 9, 0, 0), TimeSpan.FromSeconds(20));
        var statistics = statisticsService.GetStatistics();
        Assert.Equal(new[] { 2, 1 }, statistics.Select(projectStatistics => projectStatistics.LaunchCount));
        Assert.Equal(15, statistics[0].AverageDurationSeconds);
        Assert.Equal(20, statistics[0].LastDurationSeconds);
        Assert.Equal(new DateTime(2026, 10, 3, 9, 0, 0), statistics[0].LastLaunchTime);
        Assert.DoesNotContain("AverageDurationSeconds", File.ReadAllText(temporaryDirectory.Combine("launch-statistics.json")));
    }
}

public class ServiceReadinessTests
{
    [Fact]
    public async Task ServiceIsReadyWhenItsReadinessLineIsPrinted()
    {
        var command = OperatingSystem.IsWindows()
            ? new ServiceCommand("watch", "Watch", Path.GetTempPath(), "cmd.exe", new[] { "/c", "echo Rebuilding... & echo Done in 12ms & ping -n 4 127.0.0.1 >nul" }, "cmd", ReadinessPattern: @"\bDone in\b")
            : new ServiceCommand("watch", "Watch", Path.GetTempPath(), "/bin/sh", new[] { "-c", "echo Rebuilding...; echo Done in 12ms; sleep 3" }, "sh", ReadinessPattern: @"\bDone in\b");
        var hostedService = new HostedService(Path.GetTempPath(), command, killOnCloseJob: null);
        var becameReadyCount = 0;
        hostedService.BecameReady += () => Interlocked.Increment(ref becameReadyCount);
        Assert.True(hostedService.HasReadinessPattern);
        Assert.True(hostedService.Start());
        try
        {
            Assert.True(await hostedService.WaitUntilReadyAsync(TimeSpan.FromSeconds(10)));
            Assert.True(hostedService.IsReady);
            Assert.Equal(1, becameReadyCount);
        }
        finally
        {
            await hostedService.StopAsync();
        }
    }
}

public class TextOptionTests
{
    [Fact]
    public void TextOptionKeepsTheTypedValue()
    {
        var optionDefinition = new ToolOptionDefinition("custom", "Commande :", ToolOptionKind.Text, _ => Array.Empty<ToolOptionChoice>(), _ => new[] { "async" });
        var optionViewModel = new ToolOptionViewModel(optionDefinition);
        Assert.True(optionViewModel.IsText);
        Assert.False(optionViewModel.HasNoChoices);
        optionViewModel.ApplyValues(Array.Empty<string>());
        Assert.Equal("async", optionViewModel.Text);
        optionViewModel.Text = "  php bin/console app:seed  ";
        optionViewModel.ReloadChoices(new ToolOptionContext("/projet"));
        Assert.Equal(new[] { "php bin/console app:seed" }, optionViewModel.CaptureValues());
        optionViewModel.Text = " ";
        Assert.Empty(optionViewModel.CaptureValues());
    }
}
