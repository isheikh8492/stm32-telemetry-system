using System.Diagnostics;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.DataSources;

namespace Telemetry.Viewer.Services.Pipeline;

public sealed class ProcessingEngine : PollingEngine
{
    private readonly IDataSource _source;
    private readonly DataStore _store;
    // (settings.Version, eventId) — bumped whenever settings mutate or a new event arrives.
    private readonly Dictionary<Guid, (uint settingsVersion, uint eventId)> _fingerprints = new();

    private readonly object _metricsLock = new();
    private readonly Dictionary<Type, (double totalMs, long count)> _computeMetrics = new();

    public ProcessingEngine(IDataSource source, DataStore store, TimeSpan interval)
        : base(interval)
    {
        _source = source;
        _store = store;
    }

    protected override void Tick()
    {
        var id = _source.LatestEventId;
        if (id is null)
            return;

        var allSettings = _store.GetAllSettings();
        var activeIds = new HashSet<Guid>(allSettings.Count);

        foreach (var settings in allSettings)
        {
            activeIds.Add(settings.PlotId);

            var fingerprint = (settings.Version, id.Value);
            if (_fingerprints.TryGetValue(settings.PlotId, out var prev) && prev == fingerprint)
                continue;

            // Stopwatch.GetTimestamp avoids the per-iteration Stopwatch allocation.
            var startTicks = Stopwatch.GetTimestamp();
            var processed = ProcessFor(settings, _source);
            var elapsedMs = (Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;
            RecordComputeTime(settings.GetType(), elapsedMs);

            if (processed is not null)
            {
                _store.SetProcessed(settings.PlotId, processed);
                _fingerprints[settings.PlotId] = fingerprint;
            }
        }

        if (_fingerprints.Count > activeIds.Count)
        {
            var stale = _fingerprints.Keys.Where(k => !activeIds.Contains(k)).ToList();
            foreach (var k in stale)
                _fingerprints.Remove(k);
        }
    }

    public IReadOnlyDictionary<Type, double> GetAverageComputeTimes()
    {
        lock (_metricsLock)
        {
            return _computeMetrics.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.count > 0 ? kv.Value.totalMs / kv.Value.count : 0.0);
        }
    }

    public void ResetMetrics()
    {
        lock (_metricsLock)
            _computeMetrics.Clear();
    }

    private void RecordComputeTime(Type settingsType, double elapsedMs)
    {
        lock (_metricsLock)
        {
            if (_computeMetrics.TryGetValue(settingsType, out var existing))
                _computeMetrics[settingsType] = (existing.totalMs + elapsedMs, existing.count + 1);
            else
                _computeMetrics[settingsType] = (elapsedMs, 1);
        }
    }

    // Each plot type chooses its own data-access pattern: PeekLatest (cheap)
    // for "latest event only" plots, Snapshot (full copy) for plots that need history.
    private static ProcessedData? ProcessFor(PlotSettings settings, IDataSource source)
    {
        return settings.Type switch
        {
            PlotType.Oscilloscope => ProcessOscilloscope(source, (OscilloscopeSettings)settings),
            PlotType.Histogram    => ProcessHistogram(source,    (HistogramSettings)settings),
            _                     => null
        };
    }

    private static ProcessedData? ProcessOscilloscope(IDataSource source, OscilloscopeSettings settings)
    {
        var latest = source.PeekLatest();
        if (latest is null)
            return null;
        if (settings.ChannelId < 0 || settings.ChannelId >= latest.Channels.Count)
            return null;
        var channel = latest.Channels[settings.ChannelId];
        return new OscilloscopeFrame(latest.EventId, channel.Samples);
    }

    // V1: stateless trailing-window histogram. Each tick walks the ring-buffer
    // snapshot and rebuilds bin counts. Acceptable while N×ticks/sec is small;
    // when it isn't, swap to per-plot accumulator state keyed by settings.Version.
    private static ProcessedData? ProcessHistogram(IDataSource source, HistogramSettings settings)
    {
        if (settings.BinCount <= 0 || settings.MaxRange <= settings.MinRange)
            return null;

        var snapshot = source.Snapshot();
        if (snapshot.Count == 0)
            return null;

        var counts = new long[settings.BinCount];
        long total = 0;

        foreach (var ev in snapshot)
        {
            if (settings.ChannelId < 0 || settings.ChannelId >= ev.Channels.Count)
                continue;
            var p = ev.Channels[settings.ChannelId].Parameters;
            double value = settings.Param switch
            {
                ParamType.Area       => p.Area,
                ParamType.PeakHeight => p.PeakHeight,
                ParamType.PeakWidth  => p.PeakWidth,
                ParamType.Baseline   => p.Baseline,
                _                    => double.NaN,
            };
            if (double.IsNaN(value)) continue;

            var idx = settings.GetBinIndex(value);
            if (idx is null) continue;

            counts[idx.Value]++;
            total++;
        }

        var bins = new HistogramBin[settings.BinCount];
        for (int i = 0; i < settings.BinCount; i++)
        {
            var (start, end) = settings.GetBinRange(i);
            bins[i] = new HistogramBin(start, end, counts[i]);
        }
        return new HistogramFrame(total, bins);
    }
}
