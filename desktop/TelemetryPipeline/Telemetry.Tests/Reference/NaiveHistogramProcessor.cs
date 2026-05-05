using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.DataSources;
using Telemetry.Viewer.Services.Pipeline.Processors;
using Telemetry.Viewer.Views.Plots.Axes;

namespace Telemetry.Tests.Reference;

// Reference implementation: rebuilds the full bin histogram from the snapshot
// every tick. O(snapshot.Count) per Process — the cost the production
// HistogramPlotProcessor avoids with its incremental ring/bin FIFO.
//
// Used by tests to (a) validate the incremental version produces identical
// counts and pixels, and (b) provide an A/B perf number ("incremental is
// N× faster than naive recompute").
internal sealed class NaiveHistogramProcessor : IPlotProcessor
{
    public ProcessedData? Process(PlotSettings settings, IDataSource source, int pixelWidth, int pixelHeight)
    {
        var hist = (HistogramSettings)settings;
        var binCount = (int)hist.BinCount;
        var fi = ChannelDataBuffer.FeatureIndex(hist.ChannelId, hist.Param);
        var snapshot = source.GetSnapshot(fi);

        if (snapshot.IsEmpty)
            return new HistogramFrame(HistogramYAxisItem.NiceMax(0),
                Array.Empty<byte>(), pixelWidth, pixelHeight) { IsEmpty = true };

        var counts = new long[binCount];
        var strategy = AxisFactory.For(hist.Scale);
        for (long seq = snapshot.StartSequence; seq < snapshot.EndSequence; seq++)
        {
            var v = snapshot.At(seq);
            if (double.IsNaN(v)) continue;
            int bin = strategy.GetBinIndex(v, hist.MinRange, hist.MaxRange, binCount);
            if (bin >= 0) counts[bin]++;
        }

        long maxCount = 0;
        for (int i = 0; i < binCount; i++)
            if (counts[i] > maxCount) maxCount = counts[i];
        var yMax = HistogramYAxisItem.NiceMax(maxCount);

        var buffer = new byte[pixelWidth * pixelHeight * 4];
        if (binCount > 0 && yMax > 0 && pixelWidth > 0 && pixelHeight > 0)
        {
            for (int i = 0; i < binCount; i++)
            {
                var c = counts[i];
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
}
