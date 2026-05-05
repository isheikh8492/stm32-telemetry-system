using System.Diagnostics;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.DataSources;
using Telemetry.Viewer.Services.Pipeline.Processors;

namespace Telemetry.Viewer.Services.Pipeline;

// Thin orchestrator: per-plot fingerprint check, dispatch to the per-type
// PlotProcessor, store the result. All per-type knowledge lives in the
// processors; all painting happens off the UI thread inside Process().
public sealed class ProcessingEngine : PollingEngine
{
    private readonly IDataSource _source;
    private readonly DataStore _store;
    // (settingsVersion, eventId, pixelW, pixelH) — refingerprint when settings
    // mutate, a new event arrives, or the plot's data-rect is resized.
    private readonly Dictionary<Guid, (uint settingsVersion, uint eventId, int pixelW, int pixelH)> _fingerprints = new();

    // Per-plot "earliest next process time" in Stopwatch ticks. Slower plot
    // types (analysis: histogram, pseudocolor, spectral ribbon) only need
    // to refresh ~4× per second; oscilloscope wants the engine's full rate.
    private readonly Dictionary<Guid, long> _nextProcessAt = new();

    // Per-type minimum interval between Process calls. Engine still ticks
    // at its base rate; slower types just skip ticks until their interval
    // has elapsed.
    private static readonly Dictionary<PlotType, TimeSpan> ProcessingIntervals = new()
    {
        [PlotType.Oscilloscope]    = TimeSpan.FromMilliseconds(20),
        [PlotType.Histogram]       = TimeSpan.FromMilliseconds(250),
        [PlotType.Pseudocolor]     = TimeSpan.FromMilliseconds(250),
        [PlotType.SpectralRibbon]  = TimeSpan.FromMilliseconds(250),
    };

    public ProcessingEngine(IDataSource source, DataStore store, TimeSpan interval)
        : base(interval)
    {
        _source = source;
        _store = store;
    }

    protected override void Tick()
    {
        var id = _source.LatestEventId;
        if (id is null) return;

        var nowTicks = Stopwatch.GetTimestamp();
        var allSettings = _store.GetAllSettings();
        var activeIds = new HashSet<Guid>(allSettings.Count);

        foreach (var settings in allSettings)
        {
            activeIds.Add(settings.PlotId);

            // Per-type rate gate: skip if this plot isn't due yet.
            if (_nextProcessAt.TryGetValue(settings.PlotId, out var due) && nowTicks < due)
                continue;

            var (pxW, pxH) = _store.GetPixelSize(settings.PlotId);
            if (pxW <= 0 || pxH <= 0) continue;  // surface not yet sized

            var fingerprint = (settings.Version, id.Value, pxW, pxH);
            if (_fingerprints.TryGetValue(settings.PlotId, out var prev) && prev == fingerprint)
                continue;

            var processor = PlotProcessorRegistry.For(settings.Type);
            if (processor is null) continue;

            var startTicks = Stopwatch.GetTimestamp();
            var processed = processor.Process(settings, _source, pxW, pxH);
            var elapsedMs = (Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;
            RecordTime(settings.Type, elapsedMs);

            if (processed is not null)
            {
                _store.SetProcessed(settings.PlotId, processed);
                _fingerprints[settings.PlotId] = fingerprint;
            }

            // Schedule next due. Use the per-type interval; default to base
            // engine rate if a new type wasn't added to the table.
            var interval = ProcessingIntervals.TryGetValue(settings.Type, out var ti)
                ? ti : TimeSpan.FromMilliseconds(20);
            _nextProcessAt[settings.PlotId] = nowTicks + (long)(interval.TotalSeconds * Stopwatch.Frequency);
        }

        if (_fingerprints.Count > activeIds.Count)
        {
            var stale = _fingerprints.Keys.Where(k => !activeIds.Contains(k)).ToList();
            foreach (var k in stale)
            {
                _fingerprints.Remove(k);
                _nextProcessAt.Remove(k);
                // Broadcast — only the right processor will have an entry.
                foreach (var p in PlotProcessorRegistry.All)
                    p.ForgetState(k);
            }
        }
    }

}
