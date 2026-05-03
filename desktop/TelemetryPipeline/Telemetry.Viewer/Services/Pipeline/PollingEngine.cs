namespace Telemetry.Viewer.Services.Pipeline;

public abstract class PollingEngine : IDisposable
{
    private readonly TimeSpan _interval;
    private Timer? _timer;
    private int _isTicking;

    protected PollingEngine(TimeSpan interval)
    {
        _interval = interval;
    }

    public bool IsRunning => _timer is not null;

    public void Start()
    {
        if (_timer is not null)
            return;
        _timer = new Timer(_ => SafeTick(), null, _interval, _interval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose() => Stop();

    protected abstract void Tick();

    private void SafeTick()
    {
        // Reentrant guard: skip if a previous tick is still running.
        if (Interlocked.Exchange(ref _isTicking, 1) == 1)
            return;
        try
        {
            Tick();
        }
        finally
        {
            Volatile.Write(ref _isTicking, 0);
        }
    }
}
