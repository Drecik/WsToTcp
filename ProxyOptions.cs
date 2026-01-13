namespace WsToTcp;

internal sealed class ProxyOptions
{
    public ProxyOptions(TimeSpan idleTimeout)
    {
        IdleTimeout = idleTimeout;
    }

    public TimeSpan IdleTimeout { get; }
}
