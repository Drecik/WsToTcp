using System.Net.Sockets;
using System.Net.WebSockets;

namespace WsToTcp;

internal sealed class WebSocketProxy
{
    private readonly BackendConfig _config;
    private readonly ILogger<WebSocketProxy> _logger;
    private static readonly TimeSpan IdleWindow = TimeSpan.FromMinutes(5);

    public WebSocketProxy(BackendConfig config, ILogger<WebSocketProxy> logger)
    {
        _config = config;
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

        _logger.LogInformation("Session started for key {Key} -> {Host}:{Port}", routeKey, endPoint.Host, endPoint.Port);

        using var stream = tcpClient.GetStream();
        using var idle = new IdleTimeout(IdleWindow);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, idle.Token);

        var wsToTcp = RelayWebSocketToTcpAsync(webSocket, stream, linkedCts.Token, idle.Touch);
        var tcpToWs = RelayTcpToWebSocketAsync(webSocket, stream, linkedCts.Token, idle.Touch);

        var completed = await Task.WhenAny(wsToTcp, tcpToWs);
        linkedCts.Cancel();

        await Task.WhenAll(wsToTcp, tcpToWs).ContinueWith(_ => { }, TaskScheduler.Default);

        var reason = idle.IsExpired ? "idle timeout" : (completed == wsToTcp ? "client closed" : "backend closed");
        _logger.LogInformation("Session ended for key {Key} -> {Host}:{Port} ({Reason})", routeKey, endPoint.Host, endPoint.Port, reason);
    }

    private static async Task RelayWebSocketToTcpAsync(WebSocket socket, NetworkStream stream, CancellationToken token, Action onActivity)
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

    private static async Task RelayTcpToWebSocketAsync(WebSocket socket, NetworkStream stream, CancellationToken token, Action onActivity)
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
