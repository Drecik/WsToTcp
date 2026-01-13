# WsToTcp

一个使用 C# (.NET 8) 编写的 WebSocket→TCP 代理。它在 `/ws` 接受 `ws://` 连接，根据 URL 的 `Token` 参数在配置文件中查找对应后端 TCP 地址，进行双向转发。仅支持明文 `ws`，不支持 `wss`。

## 配置文件
使用键值对映射（默认文件名：`backend.config`），每行格式为 `key=host:port`，空行或以 `#` 开头的注释行会被忽略。

```text
# backend.config
example=127.0.0.1:9000
foo=192.168.1.10:7000
```

- 客户端连接时，代理从 `Token` 查询参数读取路由键（例如：`/ws?Token=example`），并在配置中查找对应后端。
- 若缺少 `Token` 或找不到对应键，返回 `400/404`。
- 通过 HTTP 刷新配置：`GET /reload` 成功返回 `200`，失败返回 `500`。

## 运行
1. 构建：`dotnet build`
2. 启动（默认地址）：`dotnet run`
3. 指定监听地址：`dotnet run --urls http://0.0.0.0:5000`
4. 指定配置文件路径：`dotnet run -- --config path/to/backend.config`
5. 指定空闲超时（秒）：`dotnet run -- --idle-timeout 300`（默认 300 秒，约 5 分钟）
6. 保护刷新接口：`dotnet run -- --reload-secret-key <key>`，之后刷新需带 `GET /reload?key=<key>` 才生效。

WebSocket 客户端示例：`ws://<host>:<port>/ws?Token=<key>`。

## 行为
- 仅接受 WebSocket 请求，HTTP 请求到 `/ws` 会返回 `400`。
- 将 WebSocket 文本/二进制消息直接转发为原始字节到 TCP；后端 TCP 字节以二进制 WebSocket 消息返回给客户端。
- 当后端 TCP 关闭时，代理向客户端发起 WebSocket 关闭握手。
- 空闲保护：在配置的空闲时间窗口内（默认 5 分钟）无双向流量，代理会主动关闭 WebSocket 与 TCP，避免句柄耗尽。
 - 状态接口：`GET /status` 返回当前连接快照（总连接数、每个连接的创建时间、最后活动时间、WebSocket 状态、客户端/后端消息与字节计数等）。
 - 健康接口：`GET /healthz` 返回服务状态与当前活动连接数。
 - 保活：服务端对 WebSocket 发送 ping（`KeepAliveInterval=30s`），帮助检测中断。

## 注意事项
- 不提供 TLS 或认证能力。
- 请确保配置行格式正确；若配置存在错误，刷新将失败并保留旧映射。
