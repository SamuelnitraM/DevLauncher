using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using DevLauncher.Services;
using DevLauncher.Services.Assistants;
using DevLauncher.Services.Elevation;
using DevLauncher.Services.Mcp;
using Xunit;

namespace DevLauncher.Tests;

public class McpRequestHandlerTests
{
    /// <summary>Automation recording the requests, with a project, a service and its log.</summary>
    private sealed class FakeAutomation : IDevLauncherAutomation
    {
        public List<string> Requests { get; } = new();

        public Task<IReadOnlyList<AutomationProject>> ListProjectsAsync()
            => Task.FromResult<IReadOnlyList<AutomationProject>>(new[] { new AutomationProject("boutique", @"C:\xampp\htdocs\boutique", "Laravel", new[] { "Défaut", "Front" }, true) });

        public Task<string> LaunchProjectAsync(string project, string? profileName)
        {
            Requests.Add($"launch {project} {profileName}");
            return Task.FromResult($"✅ Environnement lancé — {project}");
        }

        public Task<string> StopAllAsync()
        {
            Requests.Add("stop");
            return Task.FromResult("⏹ Environnement arrêté");
        }

        public Task<IReadOnlyList<AutomationService>> GetServiceStatusAsync()
            => Task.FromResult<IReadOnlyList<AutomationService>>(new[] { new AutomationService("MySQL", null, true, null), new AutomationService("Laravel Serve · boutique", "boutique", true, true) });

        public Task<IReadOnlyList<string>?> ReadServiceLogsAsync(string serviceName, int lineCount)
            => Task.FromResult<IReadOnlyList<string>?>(serviceName == "Laravel Serve · boutique" ? Enumerable.Range(1, lineCount).Select(lineNumber => $"ligne {lineNumber}").ToList() : null);

        public Task<string> RestartServiceAsync(string serviceName)
        {
            Requests.Add($"restart {serviceName}");
            return Task.FromResult($"✅ {serviceName} redémarré");
        }
    }

    private static async Task<JsonObject> SendAsync(McpRequestHandler requestHandler, string requestJson)
        => (JsonObject)JsonNode.Parse((await requestHandler.HandleAsync(requestJson))!)!;

    private static string CallTool(int requestIdentifier, string toolName, string argumentsJson = "{}")
        => $$$"""{"jsonrpc":"2.0","id":{{{requestIdentifier}}},"method":"tools/call","params":{"name":"{{{toolName}}}","arguments":{{{argumentsJson}}}}}""";

    [Fact]
    public async Task InitializationAndToolListFollowTheProtocol()
    {
        var requestHandler = new McpRequestHandler(new FakeAutomation(), "2.0.0");
        var initializeResponse = await SendAsync(requestHandler, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""");
        Assert.Equal("2025-03-26", initializeResponse["result"]!["protocolVersion"]!.GetValue<string>());
        Assert.Equal("devlauncher", initializeResponse["result"]!["serverInfo"]!["name"]!.GetValue<string>());
        Assert.NotNull(initializeResponse["result"]!["capabilities"]!["tools"]);
        Assert.Null(await requestHandler.HandleAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));
        var unknownVersionResponse = await SendAsync(requestHandler, """{"jsonrpc":"2.0","id":"a","method":"initialize","params":{"protocolVersion":"1999-01-01"}}""");
        Assert.Equal(McpRequestHandler.LatestProtocolVersion, unknownVersionResponse["result"]!["protocolVersion"]!.GetValue<string>());
        Assert.Equal("a", unknownVersionResponse["id"]!.GetValue<string>());
        var toolsResponse = await SendAsync(requestHandler, """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
        var toolNames = toolsResponse["result"]!["tools"]!.AsArray().Select(tool => tool!["name"]!.GetValue<string>()).ToList();
        Assert.Equal(new[] { "list_projects", "launch_project", "stop_all", "service_status", "read_service_logs", "restart_service" }, toolNames);
        Assert.Equal("project", toolsResponse["result"]!["tools"]![1]!["inputSchema"]!["required"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task ToolCallsReachDevLauncher()
    {
        var fakeAutomation = new FakeAutomation();
        var requestHandler = new McpRequestHandler(fakeAutomation, "2.0.0");
        string ReadText(JsonObject response) => response["result"]!["content"]![0]!["text"]!.GetValue<string>();
        bool ReadIsError(JsonObject response) => response["result"]!["isError"]!.GetValue<bool>();
        Assert.Contains("boutique", ReadText(await SendAsync(requestHandler, CallTool(3, "list_projects"))));
        Assert.Contains("Laravel Serve · boutique", ReadText(await SendAsync(requestHandler, CallTool(4, "service_status"))));
        Assert.Equal("✅ Environnement lancé — boutique", ReadText(await SendAsync(requestHandler, CallTool(5, "launch_project", """{"project":"boutique","profile":"Front"}"""))));
        Assert.True(ReadIsError(await SendAsync(requestHandler, CallTool(6, "launch_project"))));
        Assert.Equal("ligne 1\nligne 2\nligne 3", ReadText(await SendAsync(requestHandler, CallTool(7, "read_service_logs", """{"service":"Laravel Serve · boutique","lines":3}"""))));
        Assert.True(ReadIsError(await SendAsync(requestHandler, CallTool(8, "read_service_logs", """{"service":"inconnu"}"""))));
        Assert.Equal(1000, ReadText(await SendAsync(requestHandler, CallTool(9, "read_service_logs", """{"service":"Laravel Serve · boutique","lines":99999}"""))).Split('\n').Length);
        await SendAsync(requestHandler, CallTool(10, "restart_service", """{"service":"Laravel Serve · boutique"}"""));
        await SendAsync(requestHandler, CallTool(11, "stop_all"));
        Assert.Equal(new[] { "launch boutique Front", "restart Laravel Serve · boutique", "stop" }, fakeAutomation.Requests);
    }

    [Fact]
    public async Task ProtocolErrorsAreReported()
    {
        var requestHandler = new McpRequestHandler(new FakeAutomation(), "2.0.0");
        Assert.Equal(-32700, (await SendAsync(requestHandler, "{ pas du json"))["error"]!["code"]!.GetValue<int>());
        Assert.Equal(-32601, (await SendAsync(requestHandler, """{"jsonrpc":"2.0","id":1,"method":"resources/list"}"""))["error"]!["code"]!.GetValue<int>());
        Assert.Equal(-32602, (await SendAsync(requestHandler, CallTool(2, "format_disk")))["error"]!["code"]!.GetValue<int>());
        Assert.Equal(-32600, (await SendAsync(requestHandler, """{"jsonrpc":"2.0","id":3}"""))["error"]!["code"]!.GetValue<int>());
    }

    [Theory]
    [InlineData(null, "127.0.0.1:8765", true)]
    [InlineData("", "localhost:8765", true)]
    [InlineData("http://localhost:3000", "127.0.0.1:8765", true)]
    [InlineData("https://site-malveillant.example", "127.0.0.1:8765", false)]
    [InlineData(null, "attaquant.example:8765", false)]
    [InlineData(null, "127.0.0.1:9999", false)]
    [InlineData(null, "[::1]:8765", true)]
    [InlineData(null, null, false)]
    public void OnlyLocalRequestsAreAccepted(string? origin, string? host, bool isExpectedLocal)
        => Assert.Equal(isExpectedLocal, McpHttpServer.IsLocalRequest(origin, host, 8765));

    [Fact]
    public async Task ServerAnswersOverHttp()
    {
        var freePortListener = new TcpListener(IPAddress.Loopback, 0);
        freePortListener.Start();
        var port = ((IPEndPoint)freePortListener.LocalEndpoint).Port;
        freePortListener.Stop();
        using var mcpHttpServer = new McpHttpServer(new McpRequestHandler(new FakeAutomation(), "2.0.0"), new LaunchLog());
        Assert.True(mcpHttpServer.Start(port));
        using var httpClient = new HttpClient();
        var toolsResponse = await httpClient.PostAsync(McpHttpServer.BuildEndpointUrl(port),
            new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, toolsResponse.StatusCode);
        Assert.Contains("launch_project", await toolsResponse.Content.ReadAsStringAsync());
        var notificationResponse = await httpClient.PostAsync(McpHttpServer.BuildEndpointUrl(port),
            new StringContent("""{"jsonrpc":"2.0","method":"notifications/initialized"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Accepted, notificationResponse.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await httpClient.GetAsync(McpHttpServer.BuildEndpointUrl(port))).StatusCode);
        using var crossOriginRequest = new HttpRequestMessage(HttpMethod.Post, McpHttpServer.BuildEndpointUrl(port))
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json"),
        };
        crossOriginRequest.Headers.Add("Origin", "https://site-malveillant.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await httpClient.SendAsync(crossOriginRequest)).StatusCode);
    }
}

public class CommandLineQuotingTests
{
    [Theory]
    [InlineData("code", "code")]
    [InlineData(@"C:\Program Files\Microsoft VS Code\Code.exe", "\"C:\\Program Files\\Microsoft VS Code\\Code.exe\"")]
    [InlineData(@"C:\mon projet\", "\"C:\\mon projet\\\\\"")]
    [InlineData("dit \"bonjour\"", "\"dit \\\"bonjour\\\"\"")]
    [InlineData("", "\"\"")]
    public void ArgumentsAreQuotedForTheCommandLineParser(string argument, string expectedQuotedArgument)
        => Assert.Equal(expectedQuotedArgument, UnelevatedProcessStarter.QuoteArgument(argument));

    [Fact]
    public void CommandLineJoinsTheQuotedParts()
        => Assert.Equal("wt.exe -w DevLauncher new-tab -d \"C:\\mon projet\"", UnelevatedProcessStarter.BuildCommandLine("wt.exe", new[] { "-w", "DevLauncher", "new-tab", "-d", @"C:\mon projet" }));
}

public class CliAgentCatalogTests
{
    [Fact]
    public void SavedAgentSettingsAreCompleted()
    {
        var mergedSettings = CliAgentCatalog.MergeWithDefaults(new[] { new CliAgentSettings { Id = "claude-code", IsEnabled = true }, new CliAgentSettings { Id = "inconnu", IsEnabled = true } });
        Assert.Equal(CliAgentCatalog.Definitions.Select(definition => definition.Id), mergedSettings.Select(settings => settings.Id));
        Assert.True(mergedSettings.Single(settings => settings.Id == "claude-code").IsEnabled);
        Assert.False(mergedSettings.Single(settings => settings.Id == "aider").IsEnabled);
        Assert.Equal(new[] { "--continue" }, CliAgentCatalog.GetDefinition("claude-code").ContinueArguments);
    }
}
