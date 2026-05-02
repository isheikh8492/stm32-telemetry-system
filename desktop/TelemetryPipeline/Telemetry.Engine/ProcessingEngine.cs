using Telemetry.Core.Models;

namespace Telemetry.Engine;

public sealed class ProcessingEngine : PollingEngine
{
    private readonly IDataSource _source;
    private readonly DataStore _store;
    private readonly Dictionary<Guid, (PlotSettings settings, uint eventId)> _fingerprints = new();

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
        IReadOnlyList<Event>? events = null;   // lazy snapshot
        var activeIds = new HashSet<Guid>(allSettings.Count);

        foreach (var settings in allSettings)
        {
            activeIds.Add(settings.PlotId);

            // Fingerprint = (settings, eventId). Reprocess on either change.
            var fingerprint = (settings, id.Value);
            if (_fingerprints.TryGetValue(settings.PlotId, out var prev) && prev == fingerprint)
                continue;

            events ??= _source.Snapshot();
            if (events.Count == 0)
                return;

            var processed = ProcessFor(settings, events);
            if (processed is not null)
            {
                _store.SetProcessed(settings.PlotId, processed);
                _fingerprints[settings.PlotId] = fingerprint;
            }
        }

        // Drop fingerprints for plots that have been unregistered.
        if (_fingerprints.Count > activeIds.Count)
        {
            var stale = _fingerprints.Keys.Where(k => !activeIds.Contains(k)).ToList();
            foreach (var k in stale)
                _fingerprints.Remove(k);
        }
    }

    private static ProcessedData? ProcessFor(PlotSettings settings, IReadOnlyList<Event> events)
    {
        return settings switch
        {
            OscilloscopeSettings    => ProcessOscilloscope(events),
            // future plot types slot in here
            _                       => null
        };
    }

    private static ProcessedData ProcessOscilloscope(IReadOnlyList<Event> events)
    {
        var latest = events[events.Count - 1];
        return new OscilloscopeFrame(latest.EventId, latest.Samples);
    }
}
