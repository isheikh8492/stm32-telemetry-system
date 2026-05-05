using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.DataSources;
using Telemetry.Viewer.Services.Pipeline.Processors;
using Telemetry.Viewer.Views.Plots.Axes;

namespace Telemetry.Tests.Reference;

// Reference spectral ribbon: full O(snapshot.Count × channelCount) recompute
// of the per-channel bin Counts every tick. Production processor avoids this
// with per-channel ring/bin FIFOs.
internal sealed class NaiveSpectralRibbonProcessor : IPlotProcessor
{
    public ProcessedData? Process(PlotSettings settings, IDataSource source, int pixelWidth, int pixelHeight)
    {
        var s = (SpectralRibbonSettings)settings;
        var bins = (int)s.BinCount;
        var channelCount = s.ChannelIds.Count;
        if (channelCount == 0) return null;

        var indices = new int[channelCount];
        for (int ci = 0; ci < channelCount; ci++)
            indices[ci] = ChannelDataBuffer.FeatureIndex(s.ChannelIds[ci], s.Param);
        var buf = new double[channelCount][];
        var snapshot = source.GetSnapshot(indices, buf);

        if (snapshot.IsEmpty)
            return new SpectralRibbonFrame(Array.Empty<byte>(), pixelWidth, pixelHeight) { IsEmpty = true };

        var counts = new long[channelCount, bins];
        var strategy = AxisFactory.For(s.Scale);
        for (long seq = snapshot.StartSequence; seq < snapshot.EndSequence; seq++)
        {
            for (int ci = 0; ci < channelCount; ci++)
            {
                var v = snapshot.At(ci, seq);
                if (double.IsNaN(v)) continue;
                int b = strategy.GetBinIndex(v, s.MinRange, s.MaxRange, bins);
                if (b >= 0) counts[ci, b]++;
            }
        }

        long maxCount = 0;
        for (int ci = 0; ci < channelCount; ci++)
            for (int bi = 0; bi < bins; bi++)
                if (counts[ci, bi] > maxCount) maxCount = counts[ci, bi];

        var buffer = new byte[pixelWidth * pixelHeight * 4];
        if (maxCount > 0 && pixelWidth > 0 && pixelHeight > 0)
        {
            for (int ci = 0; ci < channelCount; ci++)
            {
                int x0 = (int)((long)ci       * pixelWidth / channelCount);
                int x1 = (int)((long)(ci + 1) * pixelWidth / channelCount);
                for (int bi = 0; bi < bins; bi++)
                {
                    var c = counts[ci, bi];
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
}
