using WsToTcp;

var configPath = ResolveConfigPath(args);
var idleTimeout = ResolveIdleTimeout(args);
var reloadKey = ResolveReloadKey(args);
var wsPath = ResolveWebSocketPath(args);

var builder = WebApplication.CreateBuilder(args);

// Configure console logging to include timestamps for easier tracing
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
    options.SingleLine = true;
});

builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<BackendConfig>>();
    var config = new BackendConfig(configPath, logger);
    config.TryReload(initialLoad: true);
    return config;
});

builder.Services.AddSingleton<WebSocketProxy>();
builder.Services.AddSingleton(new ProxyOptions(idleTimeout));
builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddSingleton(new ReloadOptions(reloadKey));

var app = builder.Build();

var config = app.Services.GetRequiredService<BackendConfig>();
var programLogger = app.Services.GetRequiredService<ILogger<Program>>();

app.UseWebSockets();

app.Map(wsPath, (HttpContext context, WebSocketProxy proxy) => proxy.HandleAsync(context));

// HTTP-triggered config reload endpoint (no auth; add as needed)
app.MapGet("/reload", (BackendConfig cfg, ReloadOptions opt, ILogger<Program> logger, HttpRequest req) =>
{
    if (!string.IsNullOrEmpty(opt.Key))
    {
        var provided = req.Query["key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(provided) || !string.Equals(provided, opt.Key, StringComparison.Ordinal))
        {
            return Results.Unauthorized();
        }
    }

    var ok = cfg.TryReload();
    if (ok)
    {
        logger.LogInformation("Config reloaded via HTTP endpoint: {Path}", cfg.FilePath);
        return Results.Ok(new { status = "ok", path = cfg.FilePath });
    }
    return Results.Problem(title: "reload failed", statusCode: 500);
});

// Status endpoint for debugging active connections
app.MapGet("/status", (ConnectionRegistry registry) => Results.Json(registry.GetSnapshot()));
// Simple health endpoint
app.MapGet("/healthz", (ConnectionRegistry registry) => Results.Ok(new { status = "ok", active = ((dynamic)registry.GetSnapshot()).total }));

programLogger.LogInformation("Starting WebSocket proxy. Listening on {Urls}. Config file: {ConfigPath}", string.Join(", ", app.Urls), config.FilePath);
programLogger.LogInformation("Idle timeout set to {Seconds} seconds", idleTimeout.TotalSeconds);
programLogger.LogInformation("Reload key {State}", string.IsNullOrEmpty(reloadKey) ? "disabled" : "enabled");
programLogger.LogInformation("WebSocket route path: {Path}", wsPath);

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

TimeSpan ResolveIdleTimeout(string[] appArgs)
{
    const string argKey = "--idle-timeout"; // seconds
    var index = Array.FindIndex(appArgs, s => string.Equals(s, argKey, StringComparison.OrdinalIgnoreCase));
    if (index >= 0 && index + 1 < appArgs.Length)
    {
        var value = appArgs[index + 1];
        if (double.TryParse(value, out var seconds) && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }
    }

    return TimeSpan.FromMinutes(5);
}

string? ResolveReloadKey(string[] appArgs)
{
    const string primaryArg = "--reload-secret-key";

    int index = Array.FindIndex(appArgs, s => string.Equals(s, primaryArg, StringComparison.OrdinalIgnoreCase));
    if (index >= 0 && index + 1 < appArgs.Length)
    {
        var value = appArgs[index + 1];
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
    }
    return null;
}

string ResolveWebSocketPath(string[] appArgs)
{
    const string argKey = "--ws-path";
    var index = Array.FindIndex(appArgs, s => string.Equals(s, argKey, StringComparison.OrdinalIgnoreCase));
    if (index >= 0 && index + 1 < appArgs.Length && !string.IsNullOrWhiteSpace(appArgs[index + 1]))
    {
        var value = appArgs[index + 1].Trim();
        if (!value.StartsWith("/"))
        {
            value = "/" + value;
        }
        return value;
    }

    return "/ws";
}

