namespace Telemetry.Viewer.Services.Pipeline;

public abstract class PollingEngine : IDisposable
{
    private readonly TimeSpan _interval;
    private Timer? _timer;
    private int _isTicking;

    // Per-type running totals for instrumentation. Subclasses call RecordTime
    // from inside Tick; consumers (stats VM, tests) read averages via
    // GetAverageTimes. Same shape in ProcessingEngine and RenderingEngine,
    // hence shared here.
    private readonly object _metricsLock = new();
    private readonly Dictionary<Type, (double totalMs, long count)> _metrics = new();

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

    protected void RecordTime(Type type, double elapsedMs)
    {
        lock (_metricsLock)
        {
            if (_metrics.TryGetValue(type, out var existing))
                _metrics[type] = (existing.totalMs + elapsedMs, existing.count + 1);
            else
                _metrics[type] = (elapsedMs, 1);
        }
    }

    public IReadOnlyDictionary<Type, double> GetAverageTimes()
    {
        lock (_metricsLock)
        {
            return _metrics.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.count > 0 ? kv.Value.totalMs / kv.Value.count : 0.0);
        }
    }

    public void ResetMetrics()
    {
        lock (_metricsLock)
            _metrics.Clear();
    }

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
