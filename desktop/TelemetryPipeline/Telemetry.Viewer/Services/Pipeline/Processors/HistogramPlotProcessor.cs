using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.DataSources;
using Telemetry.Viewer.Views.Plots.Axes;

namespace Telemetry.Viewer.Services.Pipeline.Processors;

// Incremental trailing-window histogram.
//
// Per-plot state holds the running bin Counts plus a parallel `int[] RingBins`
// FIFO sized to the feature buffer's capacity. When a new event arrives the
// processor:
//   1. If RingBins is full → evict head (decrement that bin's count).
//   2. Bin the new value and push to tail (increment that bin's count).
//
// Per-tick work is O(events arrived since last tick), not O(snapshot size).
// Reads `double` values straight from the feature ring — no per-event
// SelectionStrategy.TryExtract / ParamType switch.
//
// Pixel buffer is a fresh `new byte[]` per tick (the frame owns it,
// nothing else writes it after Process returns) — avoids the worker/UI
// race we saw with cached buffers.
public sealed class HistogramPlotProcessor : IPlotProcessor
{
    private readonly Dictionary<Guid, State> _states = new();

    public ProcessedData? Process(PlotSettings settings, IDataSource source, int pixelWidth, int pixelHeight)
    {
        var hist = (HistogramSettings)settings;
        var capacity = source.Capacity;
        var binCount = (int)hist.BinCount;

        if (!_states.TryGetValue(hist.PlotId, out var state) || state.Capacity != capacity || state.BinCount != binCount)
        {
            state = new State(capacity, binCount);
            _states[hist.PlotId] = state;
        }
        if (state.SettingsVersion != hist.Version)
            state.Reset(hist.Version);

        var featureIndex = ChannelDataBuffer.FeatureIndex(hist.ChannelId, hist.Param);
        var snapshot = source.GetSnapshot(featureIndex);

        // Empty snapshot — surface the IsEmpty flag so the renderer can hide
        // the bitmap rather than show a transparent buffer.
        if (snapshot.IsEmpty)
        {
            return new HistogramFrame(HistogramYAxisItem.NiceMax(0),
                Array.Empty<byte>(), pixelWidth, pixelHeight) { IsEmpty = true };
        }

        // Detect "fell behind" — events were evicted from the buffer that we
        // never processed. Wipe ring + counts and full-rebuild this tick.
        if (state.LastSequence < snapshot.StartSequence)
            state.WipeIncrementalIndex();

        var strategy = AxisFactory.For(hist.Scale);
        var min = hist.MinRange;
        var max = hist.MaxRange;

        var fromSeq = Math.Max(state.LastSequence, snapshot.StartSequence);
        for (long seq = fromSeq; seq < snapshot.EndSequence; seq++)
        {
            var v = snapshot.At(seq);
            int bin = double.IsNaN(v) ? -1 : strategy.GetBinIndex(v, min, max, binCount);
            AppendContribution(state, bin);
        }
        state.LastSequence = snapshot.EndSequence;

        // Find peak count for Y-axis ceiling.
        long maxCount = 0;
        for (int i = 0; i < binCount; i++)
            if (state.Counts[i] > maxCount) maxCount = state.Counts[i];
        var yMax = HistogramYAxisItem.NiceMax(maxCount);

        // Paint into a fresh buffer.
        var buffer = new byte[pixelWidth * pixelHeight * 4];
        if (binCount > 0 && yMax > 0 && pixelWidth > 0 && pixelHeight > 0)
        {
            for (int i = 0; i < binCount; i++)
            {
                var c = state.Counts[i];
                if (c == 0) continue;

                int x0 = (int)((long)i       * pixelWidth / binCount);
                int x1 = (int)((long)(i + 1) * pixelWidth / binCount);
                int barH = (int)(c * (long)pixelHeight / yMax);
                int y0 = pixelHeight - barH;
                PixelCanvas.FillRect(buffer, pixelWidth, pixelHeight, x0, y0, x1 - x0, barH, PixelCanvas.Black);
            }
        }

        return new HistogramFrame(yMax, buffer, pixelWidth, pixelHeight);
    }

    public void ForgetState(Guid plotId) => _states.Remove(plotId);
    public void ForgetAll() => _states.Clear();

    // FIFO append-with-eviction. RingBins[(RingStart + RingCount) % cap]
    // gets the new bin; if full, head is evicted first. Bins outside the
    // valid range (-1) are tracked but contribute nothing to Counts.
    private static void AppendContribution(State state, int bin)
    {
        if (state.RingCount == state.Capacity)
        {
            int evicted = state.RingBins[state.RingStart];
            if (evicted >= 0) state.Counts[evicted]--;
            state.RingStart = (state.RingStart + 1) % state.Capacity;
            state.RingCount--;
        }

        int writeIndex = (state.RingStart + state.RingCount) % state.Capacity;
        state.RingBins[writeIndex] = bin;
        if (bin >= 0) state.Counts[bin]++;
        state.RingCount++;
    }

    private sealed class State
    {
        public int Capacity { get; }
        public int BinCount { get; }
        public int[] RingBins;
        public long[] Counts;
        public int RingStart;
        public int RingCount;
        public long LastSequence;
        public uint SettingsVersion;

        public State(int capacity, int binCount)
        {
            Capacity = capacity;
            BinCount = binCount;
            RingBins = new int[capacity];
            Counts = new long[binCount];
        }

        public void Reset(uint settingsVersion)
        {
            Array.Clear(Counts, 0, Counts.Length);
            RingStart = 0;
            RingCount = 0;
            LastSequence = 0;
            SettingsVersion = settingsVersion;
        }

        public void WipeIncrementalIndex()
        {
            Array.Clear(Counts, 0, Counts.Length);
            RingStart = 0;
            RingCount = 0;
        }
    }
}
