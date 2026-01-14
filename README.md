# WsToTcp

A WebSocket-to-TCP proxy written in C# (.NET 8). By default it listens on `/ws` (configurable via `--ws-path`), accepts `ws://` connections, looks up backend TCP endpoints using the `Token` query parameter from a config file, and forwards traffic both ways. Supports plaintext `ws` only (no `wss`).

Chinese version: [README.cn.md](README.cn.md)

## Config file
Uses key/value mapping (default file name: `backend.config`), each line formatted as `key=host:port`. Blank lines or lines starting with `#` are ignored.

```text
# backend.config
example=127.0.0.1:9000
foo=192.168.1.10:7000
```

- On client connect, the proxy reads the route key from the `Token` query parameter (for example: `/ws?Token=example`) and looks up the backend.
- If `Token` is missing or not found, it responds with `400/404`.
- Reload config via HTTP: `GET /reload` returns `200` on success, `500` on failure.

## Run
1. Build: `dotnet build`
2. Start (default address): `dotnet run`
3. Set listen addresses: `dotnet run --urls http://0.0.0.0:5000`
4. Set config file path: `dotnet run -- --config path/to/backend.config`
5. Set idle timeout (seconds): `dotnet run -- --idle-timeout 300` (default 300 seconds, about 5 minutes)
6. Protect the reload endpoint: `dotnet run -- --reload-secret-key <key>`; subsequent reloads require `GET /reload?key=<key>`.
7. Custom WebSocket path: `dotnet run -- --ws-path /custom` (default `/ws`; a leading `/` is added automatically).

WebSocket client example: `ws://<host>:<port>/ws?Token=<key>`.

## Behavior
- Accepts only WebSocket requests; HTTP requests to the WebSocket path return `400`.
- Forwards WebSocket text/binary frames as raw bytes to TCP; backend TCP bytes are returned as binary WebSocket messages.
- When the backend TCP closes, the proxy initiates a WebSocket close handshake to the client.
- Idle protection: if there is no bidirectional traffic within the configured window (default 5 minutes), the proxy closes both WebSocket and TCP to avoid handle exhaustion.
- Status endpoint: `GET /status` returns a connection snapshot (total connections, per-connection creation time, last activity, WebSocket state, client/backend message and byte counts, etc.).
- Health endpoint: `GET /healthz` returns service status and current active connection count.

## Notes
- TLS and authentication are not provided.
- Ensure config lines are valid; if reload fails due to errors, the previous mapping remains in effect.
