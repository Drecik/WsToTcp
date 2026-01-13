using WsToTcp;

var configPath = ResolveConfigPath(args);
var idleTimeout = ResolveIdleTimeout();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<BackendConfig>>();
    var config = new BackendConfig(configPath, logger);
    config.TryReload(initialLoad: true);
    return config;
});

builder.Services.AddSingleton<WebSocketProxy>();
builder.Services.AddSingleton(new ProxyOptions(idleTimeout));

var app = builder.Build();

var config = app.Services.GetRequiredService<BackendConfig>();
var programLogger = app.Services.GetRequiredService<ILogger<Program>>();

app.UseWebSockets();

app.Map("/ws", (HttpContext context, WebSocketProxy proxy) => proxy.HandleAsync(context));

// HTTP-triggered config reload endpoint (no auth; add as needed)
app.MapGet("/reload", (BackendConfig cfg, ILogger<Program> logger) =>
{
    var ok = cfg.TryReload();
    if (ok)
    {
        logger.LogInformation("Config reloaded via HTTP endpoint: {Path}", cfg.FilePath);
        return Results.Ok(new { status = "ok", path = cfg.FilePath });
    }
    return Results.Problem(title: "reload failed", statusCode: 500);
});

programLogger.LogInformation("Starting WebSocket proxy. Listening on {Urls}. Config file: {ConfigPath}", string.Join(", ", app.Urls), config.FilePath);
programLogger.LogInformation("Idle timeout set to {Seconds} seconds", idleTimeout.TotalSeconds);

app.Run();

string ResolveConfigPath(string[] appArgs)
{
    var fromEnv = Environment.GetEnvironmentVariable("CONFIG_PATH");
    if (!string.IsNullOrWhiteSpace(fromEnv))
    {
        return Path.GetFullPath(fromEnv);
    }

    const string argKey = "--config";
    var index = Array.FindIndex(appArgs, s => string.Equals(s, argKey, StringComparison.OrdinalIgnoreCase));
    if (index >= 0 && index + 1 < appArgs.Length && !string.IsNullOrWhiteSpace(appArgs[index + 1]))
    {
        return Path.GetFullPath(appArgs[index + 1]);
    }

    return Path.GetFullPath("backend.config");
}

TimeSpan ResolveIdleTimeout()
{
    var env = Environment.GetEnvironmentVariable("IDLE_TIMEOUT_SECONDS");
    if (!string.IsNullOrWhiteSpace(env) && double.TryParse(env, out var seconds) && seconds > 0)
    {
        return TimeSpan.FromSeconds(seconds);
    }

    return TimeSpan.FromMinutes(5);
}

