using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.DataSources;

namespace Telemetry.Viewer.Services.Pipeline.Processors;

// Latest event → polyline of (sample index, ADC value), painted at the
// plot's data-rect pixel size. PeekLatest is cheap (no copy).
public sealed class OscilloscopePlotProcessor : IPlotProcessor
{
    private const int XRange = 32;       // sample count
    private const double YMax = 5000;    // ADC ceiling

    public ProcessedData? Process(PlotSettings settings, IDataSource source, int pixelWidth, int pixelHeight)
    {
        var osc = (OscilloscopeSettings)settings;
        var latest = source.PeekLatest();
        if (latest is null) return null;
        if (osc.ChannelId < 0 || osc.ChannelId >= latest.Channels.Count) return null;

        var samples = latest.Channels[osc.ChannelId].Samples;
        var buffer = new byte[pixelWidth * pixelHeight * 4];

        if (samples.Count >= 2 && pixelWidth > 1 && pixelHeight > 1)
        {
            int prevX = 0, prevY = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                int x = (int)((long)i * (pixelWidth  - 1) / (XRange - 1));
                int y = pixelHeight - 1 - (int)(samples[i] * (long)(pixelHeight - 1) / YMax);
                if (i > 0) PixelCanvas.Line(buffer, pixelWidth, pixelHeight, prevX, prevY, x, y, PixelCanvas.SteelBlue);
                prevX = x; prevY = y;
            }
        }

        return new OscilloscopeFrame(latest.EventId, samples, buffer, pixelWidth, pixelHeight);
    }
}
