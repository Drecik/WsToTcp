using System.Net;

namespace WsToTcp;

internal sealed class BackendConfig
{
    private readonly ILogger<BackendConfig> _logger;
    private readonly object _lock = new();
    private Dictionary<string, DnsEndPoint> _map = new(StringComparer.OrdinalIgnoreCase);

    public BackendConfig(string path, ILogger<BackendConfig> logger)
    {
        FilePath = path;
        _logger = logger;
    }

    public string FilePath { get; }

    public bool TryResolve(string key, out DnsEndPoint? endPoint)
    {
        lock (_lock)
        {
            return _map.TryGetValue(key, out endPoint);
        }
    }

    public bool TryReload(bool initialLoad = false)
    {
        try
        {
            var (map, count) = LoadFromDisk();
            lock (_lock)
            {
                _map = map;
            }
            _logger.LogInformation("Config {Phase} loaded from {Path} with {Count} entries", initialLoad ? "initial" : "manually", FilePath, count);
            return true;
        }
        catch (Exception ex)
        {
            var level = initialLoad ? LogLevel.Warning : LogLevel.Error;
            _logger.Log(level, ex, "Failed to load config from {Path}", FilePath);
            return false;
        }
    }

    private (Dictionary<string, DnsEndPoint> map, int count) LoadFromDisk()
    {
        if (!File.Exists(FilePath))
        {
            throw new FileNotFoundException("Config file not found", FilePath);
        }

        var lines = File.ReadAllLines(FilePath);
        var map = new Dictionary<string, DnsEndPoint>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
            {
                continue;
            }

            var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                throw new InvalidDataException($"Invalid config line: '{line}'");
            }

            var key = parts[0];
            var address = parts[1];
            var hostPort = address.Split(':', 2, StringSplitOptions.TrimEntries);
            if (hostPort.Length != 2 || !int.TryParse(hostPort[1], out var port) || port <= 0 || port > 65535)
            {
                throw new InvalidDataException($"Invalid host:port value for key '{key}'");
            }

            map[key] = new DnsEndPoint(hostPort[0], port);
        }

        return (map, map.Count);
    }
}
