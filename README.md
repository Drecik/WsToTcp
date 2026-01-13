# WsToTcp

A minimal WebSocket-to-TCP proxy written in C# (.NET 8). It accepts `ws://` connections at `/ws`, routes them to a backend TCP endpoint selected by the `a` query parameter, and relays traffic bidirectionally. TLS (`wss://`) is intentionally not supported.

## Configuration
Provide a key/value mapping file (default: `backend.config`). Each non-empty, non-comment line must be `key=host:port`.

```text
# backend.config
example=127.0.0.1:9000
foo=192.168.1.10:7000
```

- On connect, the proxy reads `a` from the WebSocket URL (e.g., `/ws?a=example`) and looks up the backend.
- Missing `a` or unknown keys return `400/404`.
- Reload the config via HTTP: `GET /reload` returns `200` on success, `500` on failure.

## Running
1. Build: `dotnet build`
2. Run (default urls): `dotnet run`
3. Customize listen urls: `dotnet run --urls http://0.0.0.0:5000`
4. Point to a custom config file: `dotnet run -- --config path/to/backend.config`
   - Alternatively set environment variable `CONFIG_PATH`.
5. Customize idle timeout (seconds): pass `--idle-timeout <seconds>` after `--`, e.g. `dotnet run -- --idle-timeout 300` (default 300 seconds).

WebSocket clients connect to `ws://<host>:<port>/ws?a=<key>`.

## Behavior
- Accepts WebSocket requests only; HTTP requests to `/ws` get `400`.
- Forwards WebSocket text/binary frames as raw bytes to TCP; backend bytes are sent as binary WebSocket messages.
- When the TCP backend closes, the proxy initiates a WebSocket close handshake to the client.
- Idle protection: if no traffic in either direction for the configured idle window (default 5 minutes), the proxy closes the WebSocket and TCP to prevent descriptor leaks. Override via CLI `--idle-timeout <seconds>`.

## Notes
- No TLS or authentication is provided.
- Keep config lines well-formed; invalid lines stop the reload and keep the previous map.
