using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.DataSources;
using Telemetry.Viewer.Views.Plots.Axes;

namespace Telemetry.Viewer.Services.Pipeline.Processors;

// Incremental spectral ribbon. Reads N feature snapshots (one per selected
// channel × current Param) in lockstep. Per-event work is O(channelCount):
// bin each channel value, append to that channel's per-channel FIFO, evict
// oldest if window is full.
public sealed class SpectralRibbonPlotProcessor : IPlotProcessor
{
    private readonly Dictionary<Guid, State> _states = new();

    public ProcessedData? Process(PlotSettings settings, IDataSource source, int pixelWidth, int pixelHeight)
    {
        var s = (SpectralRibbonSettings)settings;
        var capacity = source.Capacity;
        var bins = (int)s.BinCount;
        var channelCount = s.ChannelIds.Count;
        if (channelCount == 0) return null;

        if (!_states.TryGetValue(s.PlotId, out var state)
            || state.Capacity != capacity
            || state.Bins != bins
            || state.ChannelCount != channelCount)
        {
            state = new State(capacity, channelCount, bins);
            _states[s.PlotId] = state;
        }
        if (state.SettingsVersion != s.Version)
            state.Reset(s.Version);

        // Build feature indices for each (channelId, currentParam).
        for (int ci = 0; ci < channelCount; ci++)
            state.FeatureIndices[ci] = ChannelDataBuffer.FeatureIndex(s.ChannelIds[ci], s.Param);
        var snapshot = source.GetSnapshot(state.FeatureIndices, state.FeatureBuf);

        if (snapshot.IsEmpty)
            return new SpectralRibbonFrame(Array.Empty<byte>(), pixelWidth, pixelHeight) { IsEmpty = true };

        if (state.LastSequence < snapshot.StartSequence)
            state.WipeIncrementalIndex();

        var strategy = AxisFactory.For(s.Scale);
        var min = s.MinRange;
        var max = s.MaxRange;

        var fromSeq = Math.Max(state.LastSequence, snapshot.StartSequence);
        for (long seq = fromSeq; seq < snapshot.EndSequence; seq++)
        {
            // Evict the head event's contribution from every channel's column.
            if (state.RingCount == capacity)
            {
                for (int ci = 0; ci < channelCount; ci++)
                {
                    int eb = state.RingBins[ci, state.RingStart];
                    if (eb >= 0) state.Counts[ci, eb]--;
                }
                state.RingStart = (state.RingStart + 1) % capacity;
                state.RingCount--;
            }

            int writeIndex = (state.RingStart + state.RingCount) % capacity;
            for (int ci = 0; ci < channelCount; ci++)
            {
                var v = snapshot.At(ci, seq);
                int b = double.IsNaN(v) ? -1 : strategy.GetBinIndex(v, min, max, bins);
                state.RingBins[ci, writeIndex] = b;
                if (b >= 0) state.Counts[ci, b]++;
            }
            state.RingCount++;
        }
        state.LastSequence = snapshot.EndSequence;

        long maxCount = 0;
        for (int ci = 0; ci < channelCount; ci++)
            for (int bi = 0; bi < bins; bi++)
                if (state.Counts[ci, bi] > maxCount) maxCount = state.Counts[ci, bi];

        var buffer = new byte[pixelWidth * pixelHeight * 4];
        if (maxCount > 0 && pixelWidth > 0 && pixelHeight > 0)
        {
            for (int ci = 0; ci < channelCount; ci++)
            {
                int x0 = (int)((long)ci       * pixelWidth / channelCount);
                int x1 = (int)((long)(ci + 1) * pixelWidth / channelCount);
                for (int bi = 0; bi < bins; bi++)
                {
                    var c = state.Counts[ci, bi];
                    if (c == 0) continue;
                    int y1 = pixelHeight - (int)((long)bi       * pixelHeight / bins);
                    int y0 = pixelHeight - (int)((long)(bi + 1) * pixelHeight / bins);
                    var t = (double)c / maxCount;
                    PixelCanvas.FillRect(buffer, pixelWidth, pixelHeight,
                        x0, y0, x1 - x0, y1 - y0,
                        Colormaps.Turbo(0.15 + 0.85 * t));
                }
            }
        }

        return new SpectralRibbonFrame(buffer, pixelWidth, pixelHeight);
    }

    public void ForgetState(Guid plotId) => _states.Remove(plotId);
    public void ForgetAll() => _states.Clear();

    private sealed class State
    {
        public int Capacity { get; }
        public int ChannelCount { get; }
        public int Bins { get; }
        public int[,] RingBins;       // [channelCount, capacity]
        public long[,] Counts;        // [channelCount, bins]
        public int RingStart;
        public int RingCount;
        public long LastSequence;
        public uint SettingsVersion;
        public readonly int[] FeatureIndices;
        public readonly double[][] FeatureBuf;

        public State(int capacity, int channelCount, int bins)
        {
            Capacity = capacity;
            ChannelCount = channelCount;
            Bins = bins;
            RingBins = new int[channelCount, capacity];
            Counts = new long[channelCount, bins];
            FeatureIndices = new int[channelCount];
            FeatureBuf = new double[channelCount][];
        }

        public void Reset(uint settingsVersion)
        {
            for (int c = 0; c < ChannelCount; c++)
                for (int b = 0; b < Bins; b++) Counts[c, b] = 0;
            RingStart = 0;
            RingCount = 0;
            LastSequence = 0;
            SettingsVersion = settingsVersion;
        }

        public void WipeIncrementalIndex()
        {
            for (int c = 0; c < ChannelCount; c++)
                for (int b = 0; b < Bins; b++) Counts[c, b] = 0;
            RingStart = 0;
            RingCount = 0;
        }
    }
}
