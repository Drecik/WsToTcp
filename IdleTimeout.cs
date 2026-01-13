using System.Threading;

namespace WsToTcp;

internal sealed class IdleTimeout : IDisposable
{
    private readonly TimeSpan _duration;
    private readonly CancellationTokenSource _cts = new();
    private readonly Timer _timer;
    private bool _disposed;

    public IdleTimeout(TimeSpan duration)
    {
        _duration = duration;
        _timer = new Timer(_ =>
        {
            try { _cts.Cancel(); } catch { }
        }, null, duration, Timeout.InfiniteTimeSpan);
    }

    public CancellationToken Token => _cts.Token;

    public bool IsExpired => _cts.IsCancellationRequested;

    public void Touch()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _timer.Change(_duration, Timeout.InfiniteTimeSpan);
        }
        catch
        {
            // ignored
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
        _cts.Dispose();
    }
}
