using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.DataSources;
using Telemetry.Viewer.Views.Plots.Axes;

namespace Telemetry.Viewer.Services.Pipeline.Processors;

// Stateless trailing-window spectral ribbon. For each (channel, valueBin)
// pair, count the events whose chosen Param falls in that bin. Render as
// one column per channel, each column a Turbo-colored vertical histogram
// of counts. Empty cells transparent so the plot's white data background
// shows through.
public sealed class SpectralRibbonPlotProcessor : IPlotProcessor
{
    public ProcessedData? Process(PlotSettings settings, IDataSource source, int pixelWidth, int pixelHeight)
    {
        var s = (SpectralRibbonSettings)settings;
        var snapshot = source.Snapshot();
        if (snapshot.Count == 0) return null;

        var channelCount = s.ChannelIds.Count;
        if (channelCount == 0) return null;

        var strategy = AxisFactory.For(s.Scale);
        var bins = (int)s.BinCount;
        var min = s.MinRange;
        var max = s.MaxRange;
        var param = s.Param;

        var counts = new long[channelCount, bins];
        long total = 0;

        foreach (var ev in snapshot)
        {
            for (int ci = 0; ci < channelCount; ci++)
            {
                var channelId = s.ChannelIds[ci];
                if (channelId < 0 || channelId >= ev.Channels.Count) continue;
                if (!SelectionStrategy.TryExtractParam(ev.Channels[channelId].Parameters, param, out var value)) continue;
                var bi = strategy.GetBinIndex(value, min, max, bins);
                if (bi < 0) continue;
                counts[ci, bi]++;
                total++;
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
                    PixelCanvas.FillRect(buffer, pixelWidth, pixelHeight, x0, y0, x1 - x0, y1 - y0, Colormaps.Turbo(0.15 + 0.85 * t));
                }
            }
        }

        return new SpectralRibbonFrame(total, maxCount, buffer, pixelWidth, pixelHeight);
    }

}
