using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;

namespace WsToTcp;

internal sealed class ConnectionRegistry
{
    private readonly ConcurrentDictionary<string, ActiveConnection> _connections = new();

    public string Register(string routeKey, DnsEndPoint backend, HttpContext ctx, TimeSpan idleTimeout)
    {
        var id = Guid.NewGuid().ToString("n");
        var now = DateTimeOffset.UtcNow;
        var clientIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
        var conn = new ActiveConnection
        {
            Id = id,
            RouteKey = routeKey,
            BackendHost = backend.Host,
            BackendPort = backend.Port,
            ClientIp = clientIp,
            CreatedAt = now,
            LastActivityAt = now,
            IdleTimeoutSeconds = (int)idleTimeout.TotalSeconds,
        };
        _connections[id] = conn;
        return id;
    }

    public void Touch(string id)
    {
        if (_connections.TryGetValue(id, out var conn))
        {
            conn.LastActivityAt = DateTimeOffset.UtcNow;
        }
    }

    public void NoteWebSocketState(string id, WebSocketState state)
    {
        if (_connections.TryGetValue(id, out var conn))
        {
            conn.WebSocketState = state.ToString();
        }
    }

    public void Remove(string id)
    {
        _connections.TryRemove(id, out _);
    }

    public object GetSnapshot()
    {
        var list = _connections.Values
            .OrderBy(c => c.CreatedAt)
            .Select(c => new
            {
                c.Id,
                c.RouteKey,
                c.BackendHost,
                c.BackendPort,
                c.ClientIp,
                createdAt = c.CreatedAt,
                lastActivityAt = c.LastActivityAt,
                idleTimeoutSeconds = c.IdleTimeoutSeconds,
                webSocketState = c.WebSocketState,
                secondsSinceLastActivity = (DateTimeOffset.UtcNow - c.LastActivityAt).TotalSeconds
            })
            .ToList();

        return new
        {
            total = list.Count,
            connections = list
        };
    }

    private sealed class ActiveConnection
    {
        public string Id { get; set; } = "";
        public string RouteKey { get; set; } = "";
        public string BackendHost { get; set; } = "";
        public int BackendPort { get; set; }
        public string ClientIp { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset LastActivityAt { get; set; }
        public int IdleTimeoutSeconds { get; set; }
        public string? WebSocketState { get; set; }
    }
}
