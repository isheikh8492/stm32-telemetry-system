using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.DataSources;

namespace Telemetry.Viewer.Services.Pipeline.Processors;

// Latest event → one polyline per selected channel, each painted in its
// channel's display color. PeekLatest is cheap (no copy).
public sealed class OscilloscopePlotProcessor : IPlotProcessor
{
    private const int XRange = 32;       // sample count
    private const double YMax = 5000;    // ADC ceiling

    public ProcessedData? Process(PlotSettings settings, IDataSource source, int pixelWidth, int pixelHeight)
    {
        var osc = (OscilloscopeSettings)settings;
        var latest = source.PeekLatest();
        if (latest is null) return null;

        var buffer = new byte[pixelWidth * pixelHeight * 4];

        if (pixelWidth > 1 && pixelHeight > 1)
        {
            foreach (var channelId in osc.ChannelIds)
            {
                if (channelId < 0 || channelId >= latest.Channels.Count) continue;
                var samples = latest.Channels[channelId].Samples;
                if (samples.Count < 2) continue;

                var color = PixelCanvas.Pack(SelectionStrategy.GetChannel(channelId).Color);

                int prevX = 0, prevY = 0;
                for (int i = 0; i < samples.Count; i++)
                {
                    int x = (int)((long)i * (pixelWidth  - 1) / (XRange - 1));
                    int y = pixelHeight - 1 - (int)(samples[i] * (long)(pixelHeight - 1) / YMax);
                    if (i > 0) PixelCanvas.Line(buffer, pixelWidth, pixelHeight, prevX, prevY, x, y, color);
                    prevX = x; prevY = y;
                }
            }
        }

        // EventId from latest; Samples isn't meaningful for multi-channel so
        // we drop it from the frame contract (was unused downstream anyway).
        return new OscilloscopeFrame(latest.EventId, buffer, pixelWidth, pixelHeight);
    }
}
