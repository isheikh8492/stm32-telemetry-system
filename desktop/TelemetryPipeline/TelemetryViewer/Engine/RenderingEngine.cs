using System.Diagnostics;

namespace TelemetryViewer;

public sealed class RenderingEngine : PollingEngine
{
    private readonly SynchronizationContext _uiContext;
    private readonly DataStore _store;

    private readonly Dictionary<Guid, RenderTargetEntry> _targets = new();
    private readonly object _targetsLock = new();

    private readonly Dictionary<Guid, PendingRender> _pendingRenders = new();
    private readonly object _pendingLock = new();

    private int _renderPassScheduled;

    private readonly object _metricsLock = new();
    private readonly Dictionary<Type, (double totalMs, long count)> _renderMetrics = new();

    public RenderingEngine(SynchronizationContext uiContext, DataStore store, TimeSpan interval)
        : base(interval)
    {
        _uiContext = uiContext;
        _store = store;
    }

    public void Register(Guid plotId, IRenderTarget target)
    {
        lock (_targetsLock)
            _targets[plotId] = new RenderTargetEntry(plotId, target);
    }

    public void Unregister(Guid plotId)
    {
        lock (_targetsLock)
            _targets.Remove(plotId);
        lock (_pendingLock)
            _pendingRenders.Remove(plotId);
    }

    protected override void Tick()
    {
        RenderTargetEntry[] snapshot;
        lock (_targetsLock)
            snapshot = _targets.Values.ToArray();

        bool enqueued = false;
        foreach (var entry in snapshot)
        {
            var data = _store.GetProcessed(entry.PlotId);
            if (data is null)
                continue;
            // ReferenceEquals: ProcessingEngine writes a new ProcessedData instance only when
            // its fingerprint changes, so reference identity is the correct "is this new?" check.
            if (ReferenceEquals(data, entry.LastRenderedData))
                continue;

            lock (_pendingLock)
                _pendingRenders[entry.PlotId] = new PendingRender(entry, data);
            enqueued = true;
        }

        if (enqueued)
            ScheduleRenderPass();
    }

    public IReadOnlyDictionary<Type, double> GetAverageRenderTimes()
    {
        lock (_metricsLock)
        {
            return _renderMetrics.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.count > 0 ? kv.Value.totalMs / kv.Value.count : 0.0);
        }
    }

    public void ResetMetrics()
    {
        lock (_metricsLock)
            _renderMetrics.Clear();
    }

    private void ScheduleRenderPass()
    {
        // Coalesce: at most one UI dispatch in flight at a time.
        if (Interlocked.Exchange(ref _renderPassScheduled, 1) == 1)
            return;
        _uiContext.Post(_ => RenderPendingOnUiThread(), null);
    }

    private void RenderPendingOnUiThread()
    {
        try
        {
            List<PendingRender> pending;
            lock (_pendingLock)
            {
                pending = new List<PendingRender>(_pendingRenders.Values);
                _pendingRenders.Clear();
            }

            foreach (var item in pending)
            {
                var startTicks = Stopwatch.GetTimestamp();
                item.Entry.Target.Render(item.Data);
                var elapsedMs = (Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;
                RecordRenderTime(item.Data.GetType(), elapsedMs);

                item.Entry.LastRenderedData = item.Data;
            }
        }
        finally
        {
            Volatile.Write(ref _renderPassScheduled, 0);

            // Re-check: did more pending arrive while we were rendering?
            bool hasPending;
            lock (_pendingLock)
                hasPending = _pendingRenders.Count > 0;
            if (hasPending)
                ScheduleRenderPass();
        }
    }

    private void RecordRenderTime(Type dataType, double elapsedMs)
    {
        lock (_metricsLock)
        {
            if (_renderMetrics.TryGetValue(dataType, out var existing))
                _renderMetrics[dataType] = (existing.totalMs + elapsedMs, existing.count + 1);
            else
                _renderMetrics[dataType] = (elapsedMs, 1);
        }
    }

    private sealed class RenderTargetEntry
    {
        public Guid PlotId { get; }
        public IRenderTarget Target { get; }
        public ProcessedData? LastRenderedData;   // mutated on UI thread only

        public RenderTargetEntry(Guid plotId, IRenderTarget target)
        {
            PlotId = plotId;
            Target = target;
        }
    }

    private sealed record PendingRender(RenderTargetEntry Entry, ProcessedData Data);
}
