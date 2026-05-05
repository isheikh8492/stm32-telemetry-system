using Telemetry.Core.Models;
using Telemetry.Engine;
using Telemetry.Viewer.Models;

namespace Telemetry.Viewer.Services.DataSources;

// Pre-extracted per-(channel, param) feature rings. Replaces the old
// RingBuffer<Event> for analysis-style plots (histogram, pseudocolor,
// spectral ribbon).
//
// On Append(Event), the producer pulls every (channelId, ParamType)
// combination's value once and writes it into that feature's ring. Plots'
// processors then read `double[]` directly via `GetSnapshot(featureIndex)`,
// skipping the per-event ParamType switch + Channel list lookup that
// SelectionStrategy.TryExtract used to do every tick.
//
// FeatureIndex layout:    channelId * ParamCount + (int)ParamType
// Total feature count:    ChannelCatalog.Count * ParamCount (ParamCount = 4)
//
// Oscilloscope still uses PeekLatest (raw samples), which is preserved here
// alongside the feature rings.
public sealed class ChannelDataBuffer : IEventBuffer
{
    public const int ParamCount = 4;  // matches ParamType enum size

    private readonly object _lock = new();
    private readonly int _capacity;
    private readonly int _channelCount;
    private readonly int _featureCount;
    private readonly double[][] _featureRings;

    private int _writeIndex;
    private int _count;
    private long _totalAppended;
    private Event? _latestEvent;

    public ChannelDataBuffer(int capacity, int channelCount)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (channelCount <= 0) throw new ArgumentOutOfRangeException(nameof(channelCount));

        _capacity = capacity;
        _channelCount = channelCount;
        _featureCount = channelCount * ParamCount;
        _featureRings = new double[_featureCount][];
        for (int i = 0; i < _featureCount; i++)
            _featureRings[i] = new double[capacity];
    }

    public int Capacity => _capacity;
    public int ChannelCount => _channelCount;
    public int FeatureCount => _featureCount;

    public int Count { get { lock (_lock) return _count; } }
    public long TotalAppended { get { lock (_lock) return _totalAppended; } }
    public uint? LatestEventId { get { lock (_lock) return _latestEvent?.EventId; } }

    public Event? PeekLatest() { lock (_lock) return _latestEvent; }

    public static int FeatureIndex(int channelId, ParamType param)
        => channelId * ParamCount + (int)param;

    public void Append(Event evt)
    {
        lock (_lock)
        {
            var slot = _writeIndex;
            // Default every feature to NaN — channels not present in the
            // event leave the slot empty, processors check for NaN to skip.
            for (int fi = 0; fi < _featureCount; fi++)
                _featureRings[fi][slot] = double.NaN;

            // Index by Channel.ChannelId, NOT by list position — the event
            // may carry channels in any order or with gaps.
            for (int i = 0; i < evt.Channels.Count; i++)
            {
                var ch = evt.Channels[i];
                if ((uint)ch.ChannelId >= (uint)_channelCount) continue;
                var p = ch.Parameters;
                int baseFi = ch.ChannelId * ParamCount;
                _featureRings[baseFi + (int)ParamType.Baseline]   [slot] = p.Baseline;
                _featureRings[baseFi + (int)ParamType.Area]       [slot] = p.Area;
                _featureRings[baseFi + (int)ParamType.PeakWidth]  [slot] = p.PeakWidth;
                _featureRings[baseFi + (int)ParamType.PeakHeight] [slot] = p.PeakHeight;
            }

            _writeIndex = (slot + 1) % _capacity;
            if (_count < _capacity) _count++;
            _totalAppended++;
            _latestEvent = evt;
        }
    }

    public ChannelWindowSnapshot GetSnapshot(int featureIndex)
    {
        if ((uint)featureIndex >= (uint)_featureCount)
            return new ChannelWindowSnapshot(Array.Empty<double>(), 0, _capacity, 0, 0);

        lock (_lock)
        {
            return new ChannelWindowSnapshot(
                Values:        _featureRings[featureIndex],
                Count:         _count,
                Capacity:      _capacity,
                StartSequence: _totalAppended - _count,
                EndSequence:   _totalAppended);
        }
    }

    // Pre-allocate the Features[] array via the caller (`outFeatures`) to
    // avoid per-tick allocation for plots with many selections (Spectral
    // Ribbon over 60 channels). `outFeatures.Length` must match
    // `featureIndices.Length`.
    public MultiChannelWindowSnapshot GetSnapshot(IReadOnlyList<int> featureIndices, double[][] outFeatures)
    {
        lock (_lock)
        {
            for (int i = 0; i < featureIndices.Count; i++)
            {
                var fi = featureIndices[i];
                outFeatures[i] = (uint)fi < (uint)_featureCount
                    ? _featureRings[fi]
                    : Array.Empty<double>();
            }
            return new MultiChannelWindowSnapshot(
                Features:      outFeatures,
                Count:         _count,
                Capacity:      _capacity,
                StartSequence: _totalAppended - _count,
                EndSequence:   _totalAppended);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            for (int fi = 0; fi < _featureCount; fi++)
                Array.Clear(_featureRings[fi], 0, _capacity);
            _writeIndex = 0;
            _count = 0;
            _totalAppended = 0;
            _latestEvent = null;
        }
    }
}
