using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.DataSources;
using Telemetry.Viewer.Views.Plots.Axes;

namespace Telemetry.Viewer.Services.Pipeline.Processors;

// Stateless trailing-window 2D histogram. Walks the snapshot, builds an
// (xBin, yBin) count grid, then paints one Pbgra32 cell per bin into the
// pixel buffer. Empty cells are left transparent so the plot's white data
// background shows through; non-empty cells use the Turbo colormap
// (moderate blue → cyan → green → yellow → red → dark red) keyed off
// count / maxCount. The lowest non-zero count maps to t=0.15 so we skip
// Turbo's near-black region — a single event reads as clearly blue.
public sealed class PseudocolorPlotProcessor : IPlotProcessor
{
    public ProcessedData? Process(PlotSettings settings, IDataSource source, int pixelWidth, int pixelHeight)
    {
        var s = (PseudocolorSettings)settings;
        var snapshot = source.Snapshot();
        if (snapshot.Count == 0) return null;

        var xStrategy = AxisFactory.For(s.XScale);
        var yStrategy = AxisFactory.For(s.YScale);
        var xBins = (int)s.BinCount;
        var yBins = (int)s.BinCount;
        var xSel = s.XSelection;
        var ySel = s.YSelection;

        var counts = new long[xBins, yBins];
        long total = 0;

        foreach (var ev in snapshot)
        {
            if (!xSel.TryExtract(ev, out var xValue)) continue;
            if (!ySel.TryExtract(ev, out var yValue)) continue;
            var xi = xStrategy.GetBinIndex(xValue, s.XMinRange, s.XMaxRange, xBins);
            if (xi < 0) continue;
            var yi = yStrategy.GetBinIndex(yValue, s.YMinRange, s.YMaxRange, yBins);
            if (yi < 0) continue;
            counts[xi, yi]++;
            total++;
        }

        long maxCount = 0;
        for (int x = 0; x < xBins; x++)
            for (int y = 0; y < yBins; y++)
                if (counts[x, y] > maxCount) maxCount = counts[x, y];

        // Paint cells in pixel space. Bin (i, j) spans [i*W/Nx, (i+1)*W/Nx) ×
        // [j*H/Ny, (j+1)*H/Ny). Y is inverted (bin 0 is bottom, in viewer
        // pixel space top is y=0).
        var buffer = new byte[pixelWidth * pixelHeight * 4];
        if (maxCount > 0 && pixelWidth > 0 && pixelHeight > 0)
        {
            for (int xi = 0; xi < xBins; xi++)
            {
                int x0 = (int)((long)xi       * pixelWidth / xBins);
                int x1 = (int)((long)(xi + 1) * pixelWidth / xBins);
                for (int yi = 0; yi < yBins; yi++)
                {
                    var c = counts[xi, yi];
                    if (c == 0) continue;
                    int y1 = pixelHeight - (int)((long)yi       * pixelHeight / yBins);
                    int y0 = pixelHeight - (int)((long)(yi + 1) * pixelHeight / yBins);
                    var t = (double)c / maxCount;
                    PixelCanvas.FillRect(buffer, pixelWidth, pixelHeight, x0, y0, x1 - x0, y1 - y0, Colormaps.Turbo(0.15 + 0.85 * t));
                }
            }
        }

        return new PseudocolorFrame(total, maxCount, buffer, pixelWidth, pixelHeight);
    }

}
