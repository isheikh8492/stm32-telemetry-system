using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.DataSources;
using Telemetry.Viewer.Views.Plots.Axes;

namespace Telemetry.Viewer.Services.Pipeline.Processors;

// Worker-thread processor: per-plot pipeline (read source → compute → paint
// Pbgra32 buffer → return ProcessedData). Runs entirely off the UI thread —
// must NOT touch any UI element.
//
// Per-type logic is dispatched here on PlotSettings.Type. Each plot type
// gets a private static method; pixel primitives (FillRect, Line, Pixel)
// and Pbgra32 colors are private statics shared by all of them.
public static class PlotProcessor
{
    public static ProcessedData? Process(
        PlotSettings settings,
        IDataSource source,
        int pixelWidth,
        int pixelHeight) => settings.Type switch
    {
        PlotType.Oscilloscope => ProcessOscilloscope((OscilloscopeSettings)settings, source, pixelWidth, pixelHeight),
        PlotType.Histogram    => ProcessHistogram((HistogramSettings)settings, source, pixelWidth, pixelHeight),
        _                     => null
    };

    // ---- Oscilloscope: latest event → polyline. PeekLatest is cheap. ----
    private static ProcessedData? ProcessOscilloscope(OscilloscopeSettings settings, IDataSource source, int pxW, int pxH)
    {
        const int xRange = 32;       // sample count
        const double yMax = 5000;    // ADC ceiling

        var latest = source.PeekLatest();
        if (latest is null) return null;
        if (settings.ChannelId < 0 || settings.ChannelId >= latest.Channels.Count) return null;

        var samples = latest.Channels[settings.ChannelId].Samples;
        var buffer = new byte[pxW * pxH * 4];

        if (samples.Count >= 2 && pxW > 1 && pxH > 1)
        {
            int prevX = 0, prevY = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                int x = (int)((long)i * (pxW - 1) / (xRange - 1));
                int y = pxH - 1 - (int)(samples[i] * (long)(pxH - 1) / yMax);
                if (i > 0) Line(buffer, pxW, pxH, prevX, prevY, x, y, SteelBlue);
                prevX = x; prevY = y;
            }
        }

        return new OscilloscopeFrame(latest.EventId, samples, buffer, pxW, pxH);
    }

    // ---- Histogram: stateless trailing window. Walks snapshot, bins, paints
    // bars. YMax (the "nice" Y-axis ceiling) is computed once here — both
    // bar scaling and ScottPlot's SetLimitsY (PlotItem.OnRender) read it. ----
    private static ProcessedData? ProcessHistogram(HistogramSettings settings, IDataSource source, int pxW, int pxH)
    {
        var snapshot = source.Snapshot();
        if (snapshot.Count == 0) return null;

        var strategy = AxisFactory.For(settings.Scale);
        var binCount = (int)settings.BinCount;
        var min = settings.MinRange;
        var max = settings.MaxRange;
        var selection = settings.Selection;

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
        var buffer = new byte[pxW * pxH * 4];
        if (binCount > 0 && yMax > 0 && pxW > 0 && pxH > 0)
        {
            for (int i = 0; i < binCount; i++)
            {
                var c = counts[i];
                if (c == 0) continue;

                int x0 = (int)((long)i       * pxW / binCount);
                int x1 = (int)((long)(i + 1) * pxW / binCount);
                int barH = (int)(c * (long)pxH / yMax);
                int y0 = pxH - barH;
                FillRect(buffer, pxW, pxH, x0, y0, x1 - x0, barH, SteelBlue);
            }
        }

        return new HistogramFrame(total, bins, maxCount, yMax, buffer, pxW, pxH);
    }

    // ---- Pbgra32 colors (premultiplied, opaque). ----
    private static readonly uint SteelBlue = PackBgra(70, 130, 180, 255);

    private static uint PackBgra(byte r, byte g, byte b, byte a)
        => (uint)((a << 24) | (r << 16) | (g << 8) | b);

    // ---- Pixel primitives. Buffer is Pbgra32, width*height*4 bytes. ----

    private static void Pixel(byte[] buffer, int width, int height, int x, int y, uint bgra)
    {
        if ((uint)x >= (uint)width || (uint)y >= (uint)height) return;
        var i = (y * width + x) * 4;
        buffer[i + 0] = (byte)(bgra);
        buffer[i + 1] = (byte)(bgra >> 8);
        buffer[i + 2] = (byte)(bgra >> 16);
        buffer[i + 3] = (byte)(bgra >> 24);
    }

    private static void FillRect(byte[] buffer, int width, int height, int x, int y, int w, int h, uint bgra)
    {
        if (w <= 0 || h <= 0) return;
        var x0 = Math.Max(0, x);
        var y0 = Math.Max(0, y);
        var x1 = Math.Min(width,  x + w);
        var y1 = Math.Min(height, y + h);
        if (x1 <= x0 || y1 <= y0) return;

        byte b = (byte)(bgra);
        byte g = (byte)(bgra >> 8);
        byte r = (byte)(bgra >> 16);
        byte a = (byte)(bgra >> 24);

        for (int yy = y0; yy < y1; yy++)
        {
            int row = (yy * width + x0) * 4;
            for (int xx = x0; xx < x1; xx++)
            {
                buffer[row + 0] = b;
                buffer[row + 1] = g;
                buffer[row + 2] = r;
                buffer[row + 3] = a;
                row += 4;
            }
        }
    }

    // Bresenham. Single-pixel line; off-buffer points are clipped per Pixel().
    private static void Line(byte[] buffer, int width, int height, int x0, int y0, int x1, int y1, uint bgra)
    {
        int dx = Math.Abs(x1 - x0);
        int dy = -Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            Pixel(buffer, width, height, x0, y0, bgra);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }
}
