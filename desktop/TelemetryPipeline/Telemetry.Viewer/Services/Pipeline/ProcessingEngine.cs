using System.Diagnostics;
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

        var allSettings = _store.GetAllSettings();
        var activeIds = new HashSet<Guid>(allSettings.Count);

        foreach (var settings in allSettings)
        {
            activeIds.Add(settings.PlotId);

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
        }

        if (_fingerprints.Count > activeIds.Count)
        {
            var stale = _fingerprints.Keys.Where(k => !activeIds.Contains(k)).ToList();
            foreach (var k in stale)
                _fingerprints.Remove(k);
        }
    }

}
