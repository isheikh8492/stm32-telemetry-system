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
                    PixelCanvas.FillRect(buffer, pixelWidth, pixelHeight, x0, y0, x1 - x0, y1 - y0, TurboColor(0.15 + 0.85 * t));
                }
            }
        }

        return new PseudocolorFrame(total, maxCount, buffer, pixelWidth, pixelHeight);
    }

    // Turbo colormap (Mikhailov 2019), 8-stop piecewise linear approximation
    // of the published LUT. Blue → cyan → green → yellow → red → dark red.
    // t in [0, 1]; values outside are clamped.
    private static readonly (double t, byte r, byte g, byte b)[] TurboStops =
    {
        (0.00,  48,  18,  59),  // dark purple-blue
        (0.13,  70,  95, 230),  // blue
        (0.27,   0, 220, 255),  // cyan
        (0.40,   0, 255,  95),  // green
        (0.55, 160, 255,  30),  // lime
        (0.69, 255, 215,  30),  // yellow
        (0.83, 255,  75,  25),  // orange-red
        (1.00, 122,   4,   3),  // dark red
    };

    private static uint TurboColor(double t)
    {
        if (t <= TurboStops[0].t) { var s = TurboStops[0]; return PixelCanvas.Pack(s.r, s.g, s.b, 255); }
        if (t >= TurboStops[^1].t) { var s = TurboStops[^1]; return PixelCanvas.Pack(s.r, s.g, s.b, 255); }
        for (int i = 1; i < TurboStops.Length; i++)
        {
            var hi = TurboStops[i];
            if (t > hi.t) continue;
            var lo = TurboStops[i - 1];
            var f = (t - lo.t) / (hi.t - lo.t);
            return PixelCanvas.Pack(
                (byte)(lo.r + (hi.r - lo.r) * f),
                (byte)(lo.g + (hi.g - lo.g) * f),
                (byte)(lo.b + (hi.b - lo.b) * f),
                255);
        }
        return 0xFF000000;
    }
}
