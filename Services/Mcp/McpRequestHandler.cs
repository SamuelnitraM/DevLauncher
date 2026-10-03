using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DevLauncher.Services.Mcp;

/// <summary>
/// Answers the JSON-RPC messages of the Model Context Protocol : initialization, list of the tools, and tool calls,
/// which are forwarded to DevLauncher. Notifications get no answer.
/// </summary>
public sealed class McpRequestHandler
{
    public const string LatestProtocolVersion = "2025-06-18";
    private const int MaximumLogLineCount = 1000;
    private const int DefaultLogLineCount = 100;

    private static readonly string[] _supportedProtocolVersions = { "2025-06-18", "2025-03-26", "2024-11-05" };

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IDevLauncherAutomation _automation;
    private readonly string _serverVersion;

    public McpRequestHandler(IDevLauncherAutomation automation, string serverVersion)
    {
        _automation = automation;
        _serverVersion = serverVersion;
    }

    /// <summary>Returns the JSON answer of a message, or null for a notification.</summary>
    public async Task<string?> HandleAsync(string requestJson)
    {
        JsonObject requestObject;
        try
        {
            requestObject = JsonNode.Parse(requestJson) as JsonObject ?? throw new JsonException("objet JSON attendu");
        }
        catch (JsonException exception)
        {
            return BuildError(null, -32700, $"Parse error : {exception.Message}");
        }
        var requestIdentifier = requestObject["id"]?.DeepClone();
        if (requestObject["method"] is not JsonValue methodValue || !methodValue.TryGetValue<string>(out var method))
            return requestIdentifier is null ? null : BuildError(requestIdentifier, -32600, "Invalid Request : method manquante");
        if (requestIdentifier is null) return null;
        var parameters = requestObject["params"] as JsonObject ?? new JsonObject();
        try
        {
            JsonNode result = method switch
            {
                "initialize" => BuildInitializeResult(parameters),
                "ping" => new JsonObject(),
                "tools/list" => new JsonObject { ["tools"] = BuildToolDefinitions() },
                "tools/call" => await CallToolAsync(parameters),
                _ => throw new McpMethodNotFoundException(method),
            };
            return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = requestIdentifier, ["result"] = result }.ToJsonString();
        }
        catch (McpMethodNotFoundException)
        {
            return BuildError(requestIdentifier, -32601, $"Method not found : {method}");
        }
        catch (McpInvalidParametersException exception)
        {
            return BuildError(requestIdentifier, -32602, exception.Message);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Any failure of DevLauncher is reported to the client instead of leaving its request unanswered.
            return BuildError(requestIdentifier, -32603, $"Erreur interne : {exception.Message}");
        }
    }

    private JsonObject BuildInitializeResult(JsonObject parameters)
    {
        var requestedVersion = ReadString(parameters, "protocolVersion");
        return new JsonObject
        {
            ["protocolVersion"] = requestedVersion is not null && _supportedProtocolVersions.Contains(requestedVersion) ? requestedVersion : LatestProtocolVersion,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject { ["name"] = "devlauncher", ["title"] = "DevLauncher", ["version"] = _serverVersion },
            ["instructions"] = "DevLauncher lance les environnements de développement locaux (XAMPP, serveurs Symfony / Laravel / Node, éditeurs). "
                + "Commence par list_projects, puis launch_project ; service_status et read_service_logs aident à diagnostiquer un service en erreur.",
        };
    }

    private static JsonArray BuildToolDefinitions()
    {
        static JsonObject StringProperty(string description) => new() { ["type"] = "string", ["description"] = description };
        static JsonObject Tool(string name, string description, JsonObject properties, params string[] requiredProperties) => new()
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = new JsonArray(requiredProperties.Select(property => (JsonNode)property).ToArray()),
            },
        };
        return new JsonArray(
            Tool("list_projects", "Liste les projets connus de DevLauncher avec leur type, leurs profils de lancement et leur statut de favori.", new JsonObject()),
            Tool("launch_project", "Lance l'environnement d'un projet (éditeur, serveurs, services, navigateur) avec un profil.",
                new JsonObject
                {
                    ["project"] = StringProperty("Nom du dossier du projet ou chemin complet"),
                    ["profile"] = StringProperty("Profil de lancement, le dernier utilisé si absent"),
                },
                "project"),
            Tool("stop_all", "Arrête tout ce que DevLauncher a lancé : services, XAMPP, éditeurs du projet.", new JsonObject()),
            Tool("service_status", "Indique si Apache, MySQL, FileZilla et les services lancés par DevLauncher tournent et sont prêts.", new JsonObject()),
            Tool("read_service_logs", "Lit les dernières lignes du journal d'un service (nom tel que donné par service_status) ou du journal de lancement (« lancement »).",
                new JsonObject
                {
                    ["service"] = StringProperty("Nom du service, ou « lancement »"),
                    ["lines"] = new JsonObject { ["type"] = "integer", ["description"] = $"Nombre de lignes, {DefaultLogLineCount} par défaut, {MaximumLogLineCount} au plus", ["minimum"] = 1 },
                },
                "service"),
            Tool("restart_service", "Redémarre un service lancé par DevLauncher (serveur Symfony, Vite, worker…).",
                new JsonObject { ["service"] = StringProperty("Nom du service tel que donné par service_status") },
                "service"));
    }

    private async Task<JsonObject> CallToolAsync(JsonObject parameters)
    {
        var toolName = ReadString(parameters, "name") ?? throw new McpInvalidParametersException("Nom d'outil manquant");
        var toolArguments = parameters["arguments"] as JsonObject ?? new JsonObject();
        switch (toolName)
        {
            case "list_projects":
                return BuildToolResult(JsonSerializer.Serialize(await _automation.ListProjectsAsync(), _jsonOptions), isError: false);
            case "launch_project":
                var project = ReadString(toolArguments, "project");
                if (string.IsNullOrWhiteSpace(project)) return BuildToolResult("Argument « project » manquant", isError: true);
                return BuildToolResult(await _automation.LaunchProjectAsync(project, ReadString(toolArguments, "profile")), isError: false);
            case "stop_all":
                return BuildToolResult(await _automation.StopAllAsync(), isError: false);
            case "service_status":
                return BuildToolResult(JsonSerializer.Serialize(await _automation.GetServiceStatusAsync(), _jsonOptions), isError: false);
            case "read_service_logs":
                var serviceName = ReadString(toolArguments, "service");
                if (string.IsNullOrWhiteSpace(serviceName)) return BuildToolResult("Argument « service » manquant", isError: true);
                var lineCount = toolArguments["lines"] is JsonValue linesValue && linesValue.TryGetValue<int>(out var requestedLineCount)
                    ? Math.Clamp(requestedLineCount, 1, MaximumLogLineCount)
                    : DefaultLogLineCount;
                var logLines = await _automation.ReadServiceLogsAsync(serviceName, lineCount);
                return logLines is null
                    ? BuildToolResult($"Aucun journal ne correspond à « {serviceName} » : voir service_status", isError: true)
                    : BuildToolResult(logLines.Count == 0 ? "(journal vide)" : string.Join('\n', logLines), isError: false);
            case "restart_service":
                var restartedServiceName = ReadString(toolArguments, "service");
                if (string.IsNullOrWhiteSpace(restartedServiceName)) return BuildToolResult("Argument « service » manquant", isError: true);
                return BuildToolResult(await _automation.RestartServiceAsync(restartedServiceName), isError: false);
            default:
                throw new McpInvalidParametersException($"Outil inconnu : {toolName}");
        }
    }

    private static JsonObject BuildToolResult(string text, bool isError) => new()
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        ["isError"] = isError,
    };

    private static string? ReadString(JsonObject jsonObject, string propertyName)
        => jsonObject[propertyName] is JsonValue propertyValue && propertyValue.TryGetValue<string>(out var text) ? text : null;

    private static string BuildError(JsonNode? requestIdentifier, int errorCode, string errorMessage)
        => new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = requestIdentifier,
            ["error"] = new JsonObject { ["code"] = errorCode, ["message"] = errorMessage },
        }.ToJsonString();

    private sealed class McpMethodNotFoundException : Exception
    {
        public McpMethodNotFoundException(string method) : base(method)
        {
        }
    }

    private sealed class McpInvalidParametersException : Exception
    {
        public McpInvalidParametersException(string message) : base(message)
        {
        }
    }
}
