using System.IO;
using System.Net;
using System.Text;

namespace DevLauncher.Services.Mcp;

/// <summary>
/// Local MCP server over HTTP (« Streamable HTTP » transport, JSON answers) at http://127.0.0.1:port/mcp.
/// It listens on the loopback only, requires the access token of the settings (Authorization: Bearer), and refuses
/// the requests coming from a web page (Origin header) or sent to another host name : DevLauncher runs as administrator,
/// only the AI clients configured by the user may drive it.
/// </summary>
public sealed class McpHttpServer : IDisposable
{
    public const string EndpointPath = "/mcp";

    private readonly McpRequestHandler _requestHandler;
    private readonly LaunchLog _launchLog;
    private readonly Func<string> _getAccessToken;
    private HttpListener? _httpListener;
    private int _port;

    public McpHttpServer(McpRequestHandler requestHandler, LaunchLog launchLog, Func<string> getAccessToken)
    {
        _requestHandler = requestHandler;
        _launchLog = launchLog;
        _getAccessToken = getAccessToken;
    }

    /// <summary>Random token generated once per installation.</summary>
    public static string GenerateAccessToken() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    /// <summary>True when the Authorization header carries the expected token, compared in constant time.</summary>
    public static bool IsAuthorized(string? authorizationHeader, string accessToken)
    {
        if (string.IsNullOrEmpty(accessToken) || authorizationHeader is null) return false;
        var expectedHeader = Encoding.UTF8.GetBytes($"Bearer {accessToken}");
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(authorizationHeader.Trim()), expectedHeader);
    }

    public bool IsRunning => _httpListener?.IsListening == true;

    public static string BuildEndpointUrl(int port) => $"http://127.0.0.1:{port}{EndpointPath}";

    /// <summary>Starts listening on the port. Returns false when the port cannot be used (logged).</summary>
    public bool Start(int port)
    {
        Stop();
        var httpListener = new HttpListener();
        httpListener.Prefixes.Add($"http://127.0.0.1:{port}{EndpointPath}/");
        httpListener.Prefixes.Add($"http://localhost:{port}{EndpointPath}/");
        try
        {
            httpListener.Start();
        }
        catch (HttpListenerException exception)
        {
            _launchLog.Error($"❌ Serveur MCP indisponible sur le port {port} : {exception.Message}");
            httpListener.Close();
            return false;
        }
        _httpListener = httpListener;
        _port = port;
        _ = AcceptRequestsAsync(httpListener);
        _launchLog.Info($"🤖 Serveur MCP à l'écoute : {BuildEndpointUrl(port)}");
        return true;
    }

    public void Stop()
    {
        if (_httpListener is null) return;
        _httpListener.Close();
        _httpListener = null;
    }

    /// <summary>Each request is answered on its own task, so that a long launch does not block the other requests.</summary>
    private async Task AcceptRequestsAsync(HttpListener httpListener)
    {
        while (httpListener.IsListening)
        {
            HttpListenerContext requestContext;
            try
            {
                requestContext = await httpListener.GetContextAsync();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }
            _ = AnswerAsync(requestContext);
        }
    }

    private async Task AnswerAsync(HttpListenerContext requestContext)
    {
        var request = requestContext.Request;
        var response = requestContext.Response;
        try
        {
            if (!IsLocalRequest(request.Headers["Origin"], request.Headers["Host"], _port))
            {
                await WriteResponseAsync(response, HttpStatusCode.Forbidden, null);
                return;
            }
            if (!IsAuthorized(request.Headers["Authorization"], _getAccessToken()))
            {
                response.AddHeader("WWW-Authenticate", "Bearer");
                await WriteResponseAsync(response, HttpStatusCode.Unauthorized, null);
                return;
            }
            if (request.HttpMethod != "POST")
            {
                // No server-initiated stream : the GET stream of the transport is not offered.
                response.AddHeader("Allow", "POST");
                await WriteResponseAsync(response, HttpStatusCode.MethodNotAllowed, null);
                return;
            }
            string requestBody;
            using (var bodyReader = new StreamReader(request.InputStream, Encoding.UTF8))
                requestBody = await bodyReader.ReadToEndAsync();
            var responseBody = await _requestHandler.HandleAsync(requestBody);
            await WriteResponseAsync(response, responseBody is null ? HttpStatusCode.Accepted : HttpStatusCode.OK, responseBody);
        }
        catch (Exception exception) when (exception is HttpListenerException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            _launchLog.Detail($"Serveur MCP : requête interrompue ({exception.Message})");
            // The client is not left waiting on a request that failed half-way.
            try
            {
                response.Abort();
            }
            catch (ObjectDisposedException)
            {
                // Already closed.
            }
        }
    }

    /// <summary>Accepts the requests without Origin, or from a local origin, sent to the loopback host names.</summary>
    public static bool IsLocalRequest(string? origin, string? host, int port)
    {
        static bool IsLoopbackHost(string hostName) => hostName is "127.0.0.1" or "localhost" or "[::1]";
        if (!string.IsNullOrEmpty(origin) && (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri) || !IsLoopbackHost(originUri.Host))) return false;
        if (string.IsNullOrEmpty(host)) return false;
        var hostParts = host.Split(':');
        var hostName = host.StartsWith('[') ? host[..(host.IndexOf(']') + 1)] : hostParts[0];
        var hostPort = host.StartsWith('[') ? host[(host.IndexOf(']') + 1)..].TrimStart(':') : hostParts.ElementAtOrDefault(1);
        return IsLoopbackHost(hostName.ToLowerInvariant()) && (string.IsNullOrEmpty(hostPort) || hostPort == port.ToString());
    }

    private static async Task WriteResponseAsync(HttpListenerResponse response, HttpStatusCode statusCode, string? responseBody)
    {
        response.StatusCode = (int)statusCode;
        if (responseBody is not null)
        {
            var responseBytes = Encoding.UTF8.GetBytes(responseBody);
            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = responseBytes.Length;
            await response.OutputStream.WriteAsync(responseBytes);
        }
        response.Close();
    }

    public void Dispose() => Stop();
}

/// <summary>Access token of the MCP server, kept in the settings.</summary>
public static class McpAccessToken
{
    /// <summary>Generates and saves the token when the settings hold none.</summary>
    public static void EnsureGenerated()
    {
        if (!string.IsNullOrWhiteSpace(AppSettings.McpServerToken)) return;
        AppSettings.McpServerToken = McpHttpServer.GenerateAccessToken();
        try
        {
            SettingsService.Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The token works for this session and is saved with the next settings.
        }
    }

    /// <summary>Command registering the server in Claude Code.</summary>
    public static string BuildClaudeCodeCommand(int port, string accessToken)
        => $"claude mcp add --transport http devlauncher {McpHttpServer.BuildEndpointUrl(port)} --header \"Authorization: Bearer {accessToken}\"";
}
