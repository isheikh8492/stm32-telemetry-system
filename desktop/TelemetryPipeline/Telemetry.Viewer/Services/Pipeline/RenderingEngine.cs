using System.Diagnostics;
using Telemetry.Viewer.Models;

namespace Telemetry.Viewer.Services.Pipeline;

public sealed class RenderingEngine : PollingEngine
{
    private readonly SynchronizationContext _uiContext;
    private readonly DataStore _store;

    private readonly Dictionary<Guid, RenderTargetEntry> _targets = new();
    private readonly object _targetsLock = new();

    private readonly Dictionary<Guid, PendingRender> _pendingRenders = new();
    private readonly object _pendingLock = new();

    private int _renderPassScheduled;

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
                RecordTime(item.Data.GetType(), elapsedMs);

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
