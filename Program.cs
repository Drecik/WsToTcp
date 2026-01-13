using WsToTcp;

var configPath = ResolveConfigPath(args);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<BackendConfig>>();
    var config = new BackendConfig(configPath, logger);
    config.TryReload(initialLoad: true);
    return config;
});

builder.Services.AddSingleton<WebSocketProxy>();

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

