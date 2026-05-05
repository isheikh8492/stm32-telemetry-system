using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.DataSources;
using Telemetry.Viewer.Services.Pipeline.Processors;
using Telemetry.Viewer.Views.Plots.Axes;

namespace Telemetry.Tests.Reference;

// Reference 2D-histogram impl that rebuilds the bins[bins,bins] grid from
// scratch every Process call. Mirrors the production processor's pixel paint
// so an A/B against PseudocolorPlotProcessor compares both correctness AND
// per-tick cost.
internal sealed class NaivePseudocolorProcessor : IPlotProcessor
{
    public ProcessedData? Process(PlotSettings settings, IDataSource source, int pixelWidth, int pixelHeight)
    {
        var s = (PseudocolorSettings)settings;
        var bins = (int)s.BinCount;

        var indices = new[]
        {
            ChannelDataBuffer.FeatureIndex(s.XChannelId, s.XParam),
            ChannelDataBuffer.FeatureIndex(s.YChannelId, s.YParam),
        };
        var buf = new double[2][];
        var snapshot = source.GetSnapshot(indices, buf);

        if (snapshot.IsEmpty)
            return new PseudocolorFrame(Array.Empty<byte>(), pixelWidth, pixelHeight) { IsEmpty = true };

        var counts = new long[bins, bins];
        var xStrat = AxisFactory.For(s.XScale);
        var yStrat = AxisFactory.For(s.YScale);
        for (long seq = snapshot.StartSequence; seq < snapshot.EndSequence; seq++)
        {
            var xv = snapshot.At(0, seq);
            var yv = snapshot.At(1, seq);
            if (double.IsNaN(xv) || double.IsNaN(yv)) continue;
            int xb = xStrat.GetBinIndex(xv, s.XMinRange, s.XMaxRange, bins);
            int yb = yStrat.GetBinIndex(yv, s.YMinRange, s.YMaxRange, bins);
            if (xb >= 0 && yb >= 0) counts[xb, yb]++;
        }

        long maxCount = 0;
        for (int x = 0; x < bins; x++)
            for (int y = 0; y < bins; y++)
                if (counts[x, y] > maxCount) maxCount = counts[x, y];

        var buffer = new byte[pixelWidth * pixelHeight * 4];
        if (maxCount > 0 && pixelWidth > 0 && pixelHeight > 0)
        {
            for (int xi = 0; xi < bins; xi++)
            {
                int x0 = (int)((long)xi       * pixelWidth / bins);
                int x1 = (int)((long)(xi + 1) * pixelWidth / bins);
                for (int yi = 0; yi < bins; yi++)
                {
                    var c = counts[xi, yi];
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

        return new PseudocolorFrame(buffer, pixelWidth, pixelHeight);
    }
}
