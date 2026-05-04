using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.DataSources;
using Telemetry.Viewer.Views.Plots.Axes;

namespace Telemetry.Viewer.Services.Pipeline.Processors;

// Stateless trailing-window histogram. Walks the snapshot, builds bin counts,
// then paints one filled rectangle per bin into the pixel buffer.
//
// YMax (the "nice" Y-axis ceiling) is computed once here — both the bar
// scaling and ScottPlot's SetLimitsY (PlotItem.OnRender) read it.
public sealed class HistogramPlotProcessor : IPlotProcessor
{
    public ProcessedData? Process(PlotSettings settings, IDataSource source, int pixelWidth, int pixelHeight)
    {
        var hist = (HistogramSettings)settings;
        var snapshot = source.Snapshot();
        if (snapshot.Count == 0) return null;

        var strategy = AxisFactory.For(hist.Scale);
        var binCount = (int)hist.BinCount;
        var min = hist.MinRange;
        var max = hist.MaxRange;
        var selection = hist.Selection;

        var counts = new long[binCount];
        long total = 0;

        foreach (var ev in snapshot)
        {
            if (!selection.TryExtract(ev, out var value)) continue;
            var idx = strategy.GetBinIndex(value, min, max, binCount);
            if (idx < 0) continue;
            counts[idx]++;
            total++;
        }

        long maxCount = 0;
        var bins = new HistogramBin[binCount];
        for (int i = 0; i < binCount; i++)
        {
            var (start, end) = strategy.GetBinRange(i, min, max, binCount);
            var c = counts[i];
            if (c > maxCount) maxCount = c;
            bins[i] = new HistogramBin(start, end, c);
        }

        var yMax = HistogramYAxisItem.NiceMax(maxCount);

        // Paint bars in pixel space. Bin i spans [i, i+1) in bin-position
        // space → [i*W/N, (i+1)*W/N) in pixels. Bar height is c/yMax.
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
                PixelCanvas.FillRect(buffer, pixelWidth, pixelHeight, x0, y0, x1 - x0, barH, PixelCanvas.SteelBlue);
            }
        }

        return new HistogramFrame(total, bins, maxCount, yMax, buffer, pixelWidth, pixelHeight);
    }
}
