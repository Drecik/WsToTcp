using System.Net.Sockets;
using System.Net.WebSockets;

namespace WsToTcp;

internal sealed class WebSocketProxy
{
    private readonly BackendConfig _config;
    private readonly ILogger<WebSocketProxy> _logger;
    private readonly ProxyOptions _options;
    private readonly ConnectionRegistry _registry;

    public WebSocketProxy(BackendConfig config, ProxyOptions options, ConnectionRegistry registry, ILogger<WebSocketProxy> logger)
    {
        _config = config;
        _options = options;
        _registry = registry;
        _logger = logger;
    }

    public async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("WebSocket requests only");
            return;
        }

        var routeKey = context.Request.Query["Token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(routeKey))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Missing 'Token' query parameter");
            return;
        }

        if (!_config.TryResolve(routeKey, out var endPoint) || endPoint is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync("No backend for supplied key");
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        using var tcpClient = new TcpClient();

        try
        {
            await tcpClient.ConnectAsync(endPoint.Host, endPoint.Port, context.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to backend {Host}:{Port}", endPoint.Host, endPoint.Port);
            await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, "Backend unavailable", CancellationToken.None);
            return;
        }

        var connId = _registry.Register(routeKey, endPoint, context, _options.IdleTimeout);
        _logger.LogInformation("Session started [{Id}] for key {Key} -> {Host}:{Port}", connId, routeKey, endPoint.Host, endPoint.Port);

        using var stream = tcpClient.GetStream();
        using var idle = new IdleTimeout(_options.IdleTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, idle.Token);

        void Activity()
        {
            idle.Touch();
            _registry.Touch(connId);
            _registry.NoteWebSocketState(connId, webSocket.State);
        }

        var wsToTcp = RelayWebSocketToTcpAsync(webSocket, stream, linkedCts.Token, Activity, bytes => _registry.AddClientData(connId, bytes));
        var tcpToWs = RelayTcpToWebSocketAsync(webSocket, stream, linkedCts.Token, Activity, bytes => _registry.AddBackendData(connId, bytes));

        var completed = await Task.WhenAny(wsToTcp, tcpToWs);
        linkedCts.Cancel();

        await Task.WhenAll(wsToTcp, tcpToWs).ContinueWith(_ => { }, TaskScheduler.Default);

        var reason = idle.IsExpired ? "idle timeout" : (completed == wsToTcp ? "client closed" : "backend closed");
        _logger.LogInformation("Session ended [{Id}] for key {Key} -> {Host}:{Port} ({Reason})", connId, routeKey, endPoint.Host, endPoint.Port, reason);
        _registry.Remove(connId);
    }

    private static async Task RelayWebSocketToTcpAsync(WebSocket socket, NetworkStream stream, CancellationToken token, Action onActivity, Action<int> onClientBytes)
    {
        var buffer = new byte[8192];
        while (!token.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(buffer, token);
            }
            catch
            {
                break;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            if (result.MessageType == WebSocketMessageType.Text || result.MessageType == WebSocketMessageType.Binary)
            {
                onActivity();
                onClientBytes(result.Count);
                await stream.WriteAsync(buffer.AsMemory(0, result.Count), token);
            }
        }

        try
        {
            stream.Close();
        }
        catch
        {
            // ignored
        }
    }

    private static async Task RelayTcpToWebSocketAsync(WebSocket socket, NetworkStream stream, CancellationToken token, Action onActivity, Action<int> onBackendBytes)
    {
        var buffer = new byte[8192];
        while (!token.IsCancellationRequested)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buffer, token);
            }
            catch
            {
                break;
            }

            if (read == 0)
            {
                break;
            }

            if (socket.State == WebSocketState.Open)
            {
                onActivity();
                onBackendBytes(read);
                await socket.SendAsync(buffer.AsMemory(0, read), WebSocketMessageType.Binary, endOfMessage: true, cancellationToken: token);
            }
            else
            {
                break;
            }
        }

        try
        {
            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
            {
                // Initiate WebSocket close handshake when backend closes
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Backend closed", CancellationToken.None);
            }
        }
        catch
        {
            // ignored
        }
    }
}
