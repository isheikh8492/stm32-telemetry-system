using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.DataSources;
using Telemetry.Viewer.Views.Plots.Axes;

namespace Telemetry.Viewer.Services.Pipeline.Processors;

// Incremental 2D histogram (heatmap). Reads two synchronized feature
// snapshots (X, Y) — same trick as Histogram but with paired (xBin, yBin)
// FIFOs. RingX/RingY parallel-track each event's bin assignment so when
// an event is evicted from the window we know exactly which (x, y) cell
// to decrement.
public sealed class PseudocolorPlotProcessor : IPlotProcessor
{
    private readonly Dictionary<Guid, State> _states = new();

    public ProcessedData? Process(PlotSettings settings, IDataSource source, int pixelWidth, int pixelHeight)
    {
        var s = (PseudocolorSettings)settings;
        var capacity = source.Capacity;
        var bins = (int)s.BinCount;

        if (!_states.TryGetValue(s.PlotId, out var state) || state.Capacity != capacity || state.Bins != bins)
        {
            state = new State(capacity, bins);
            _states[s.PlotId] = state;
        }
        if (state.SettingsVersion != s.Version)
            state.Reset(s.Version);

        var xFi = ChannelDataBuffer.FeatureIndex(s.XChannelId, s.XParam);
        var yFi = ChannelDataBuffer.FeatureIndex(s.YChannelId, s.YParam);
        state.FeatureBuf[0] = null!;
        state.FeatureBuf[1] = null!;
        state.FeatureIndices[0] = xFi;
        state.FeatureIndices[1] = yFi;
        var snapshot = source.GetSnapshot(state.FeatureIndices, state.FeatureBuf);

        if (snapshot.IsEmpty)
            return new PseudocolorFrame(0, 0, Array.Empty<byte>(), pixelWidth, pixelHeight) { IsEmpty = true };

        if (state.LastSequence < snapshot.StartSequence)
            state.WipeIncrementalIndex();

        var xStrategy = AxisFactory.For(s.XScale);
        var yStrategy = AxisFactory.For(s.YScale);

        var fromSeq = Math.Max(state.LastSequence, snapshot.StartSequence);
        for (long seq = fromSeq; seq < snapshot.EndSequence; seq++)
        {
            var xv = snapshot.At(0, seq);
            var yv = snapshot.At(1, seq);
            int xb = -1, yb = -1;
            if (!double.IsNaN(xv) && !double.IsNaN(yv))
            {
                xb = xStrategy.GetBinIndex(xv, s.XMinRange, s.XMaxRange, bins);
                yb = yStrategy.GetBinIndex(yv, s.YMinRange, s.YMaxRange, bins);
                if (xb < 0 || yb < 0) { xb = -1; yb = -1; }
            }
            AppendContribution(state, xb, yb);
        }
        state.LastSequence = snapshot.EndSequence;

        long maxCount = 0;
        for (int x = 0; x < bins; x++)
            for (int y = 0; y < bins; y++)
                if (state.Counts[x, y] > maxCount) maxCount = state.Counts[x, y];

        var buffer = new byte[pixelWidth * pixelHeight * 4];
        if (maxCount > 0 && pixelWidth > 0 && pixelHeight > 0)
        {
            for (int xi = 0; xi < bins; xi++)
            {
                int x0 = (int)((long)xi       * pixelWidth / bins);
                int x1 = (int)((long)(xi + 1) * pixelWidth / bins);
                for (int yi = 0; yi < bins; yi++)
                {
                    var c = state.Counts[xi, yi];
                    if (c == 0) continue;
                    int y1 = pixelHeight - (int)((long)yi       * pixelHeight / bins);
                    int y0 = pixelHeight - (int)((long)(yi + 1) * pixelHeight / bins);
                    var t = (double)c / maxCount;
                    PixelCanvas.FillRect(buffer, pixelWidth, pixelHeight,
                        x0, y0, x1 - x0, y1 - y0,
                        Colormaps.Turbo(0.15 + 0.85 * t));
                }
            }
        }

        return new PseudocolorFrame(state.RingCount, maxCount, buffer, pixelWidth, pixelHeight);
    }

    public void ForgetState(Guid plotId) => _states.Remove(plotId);

    private static void AppendContribution(State state, int xb, int yb)
    {
        if (state.RingCount == state.Capacity)
        {
            int ex = state.RingX[state.RingStart];
            int ey = state.RingY[state.RingStart];
            if (ex >= 0 && ey >= 0) state.Counts[ex, ey]--;
            state.RingStart = (state.RingStart + 1) % state.Capacity;
            state.RingCount--;
        }
        int writeIndex = (state.RingStart + state.RingCount) % state.Capacity;
        state.RingX[writeIndex] = xb;
        state.RingY[writeIndex] = yb;
        if (xb >= 0 && yb >= 0) state.Counts[xb, yb]++;
        state.RingCount++;
    }

    private sealed class State
    {
        public int Capacity { get; }
        public int Bins { get; }
        public int[] RingX;
        public int[] RingY;
        public long[,] Counts;
        public int RingStart;
        public int RingCount;
        public long LastSequence;
        public uint SettingsVersion;
        // Pre-allocated arrays for IDataSource.GetSnapshot's outFeatures parameter.
        public readonly int[] FeatureIndices = new int[2];
        public readonly double[][] FeatureBuf = new double[2][];

        public State(int capacity, int bins)
        {
            Capacity = capacity;
            Bins = bins;
            RingX = new int[capacity];
            RingY = new int[capacity];
            Counts = new long[bins, bins];
        }

        public void Reset(uint settingsVersion)
        {
            for (int x = 0; x < Bins; x++) for (int y = 0; y < Bins; y++) Counts[x, y] = 0;
            RingStart = 0;
            RingCount = 0;
            LastSequence = 0;
            SettingsVersion = settingsVersion;
        }

        public void WipeIncrementalIndex()
        {
            for (int x = 0; x < Bins; x++) for (int y = 0; y < Bins; y++) Counts[x, y] = 0;
            RingStart = 0;
            RingCount = 0;
        }
    }
}
