using System.Diagnostics;
using System.Windows.Threading;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;

namespace Telemetry.Viewer.Services.Pipeline;

public sealed class RenderingEngine : PollingEngine
{
    private readonly Dispatcher _dispatcher;
    private readonly DataStore _store;

    private readonly Dictionary<Guid, RenderTargetEntry> _targets = new();
    private readonly object _targetsLock = new();

    private readonly Dictionary<Guid, PendingRender> _pendingRenders = new();
    private readonly object _pendingLock = new();

    private int _renderPassScheduled;

    // Per-plot "earliest next render time". Slower plot types update at a
    // slower visual rate; fast types (oscilloscope) get the engine's full rate.
    private readonly Dictionary<Guid, long> _nextRenderAt = new();
    private readonly object _nextRenderLock = new();

    private static readonly Dictionary<PlotType, TimeSpan> RenderingIntervals = new()
    {
        [PlotType.Oscilloscope]    = TimeSpan.FromMilliseconds(33),
        [PlotType.Histogram]       = TimeSpan.FromMilliseconds(250),
        [PlotType.Pseudocolor]     = TimeSpan.FromMilliseconds(250),
        [PlotType.SpectralRibbon]  = TimeSpan.FromMilliseconds(250),
    };

    public RenderingEngine(Dispatcher dispatcher, DataStore store, TimeSpan interval)
        : base(interval)
    {
        _dispatcher = dispatcher;
        _store = store;
    }

    public void Register(Guid plotId, IRenderTarget target)
    {
        // Cache the plot type at registration time. Register runs on the UI
        // thread (called from PlotItemHost.OnLoaded → Worksheet.OnPlotItemReady
        // → ViewportSession.AddPlot) where DataContext access is allowed.
        // Reading target.Settings on the worker-thread Tick later would
        // throw because DataContext is a DependencyProperty.
        var type = target.Settings.Type;
        lock (_targetsLock)
            _targets[plotId] = new RenderTargetEntry(plotId, target, type);
    }

    public void Unregister(Guid plotId)
    {
        lock (_targetsLock)
            _targets.Remove(plotId);
        lock (_pendingLock)
            _pendingRenders.Remove(plotId);
        lock (_nextRenderLock)
            _nextRenderAt.Remove(plotId);
    }

    protected override void Tick()
    {
        RenderTargetEntry[] snapshot;
        lock (_targetsLock)
            snapshot = _targets.Values.ToArray();

        var nowTicks = Stopwatch.GetTimestamp();

        bool enqueued = false;
        foreach (var entry in snapshot)
        {
            // Per-type rate gate: skip if not yet due.
            lock (_nextRenderLock)
            {
                if (_nextRenderAt.TryGetValue(entry.PlotId, out var due) && nowTicks < due)
                    continue;
            }

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

            var interval = RenderingIntervals.TryGetValue(entry.Type, out var ti)
                ? ti : TimeSpan.FromMilliseconds(33);
            lock (_nextRenderLock)
                _nextRenderAt[entry.PlotId] = nowTicks + (long)(interval.TotalSeconds * Stopwatch.Frequency);
        }

        if (enqueued)
            ScheduleRenderPass();
    }

    private void ScheduleRenderPass()
    {
        // Coalesce: at most one UI dispatch in flight at a time.
        if (Interlocked.Exchange(ref _renderPassScheduled, 1) == 1)
            return;
        _dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(RenderPendingOnUiThread));
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
                RecordTime(item.Entry.Type, elapsedMs);

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

    // Wipe every registered target's visual + drop pending/last-rendered state
    // so the next ProcessedData (post-clear) renders fresh. Marshals the per-
    // target Clear() onto the UI thread.
    public void ClearAll()
    {
        RenderTargetEntry[] snapshot;
        lock (_targetsLock)
            snapshot = _targets.Values.ToArray();

        lock (_pendingLock) _pendingRenders.Clear();
        lock (_nextRenderLock) _nextRenderAt.Clear();
        foreach (var entry in snapshot)
            entry.LastRenderedData = null;

        _dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            foreach (var entry in snapshot)
                entry.Target.Clear();
        }));
    }

    private sealed class RenderTargetEntry
    {
        public Guid PlotId { get; }
        public IRenderTarget Target { get; }
        public PlotType Type { get; }
        public ProcessedData? LastRenderedData;   // mutated on UI thread only

        public RenderTargetEntry(Guid plotId, IRenderTarget target, PlotType type)
        {
            PlotId = plotId;
            Target = target;
            Type = type;
        }
    }

    private sealed record PendingRender(RenderTargetEntry Entry, ProcessedData Data);
}
