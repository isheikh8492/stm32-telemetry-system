namespace Telemetry.Viewer.Models.Plots;

// YMax is published so PlotItem can decide whether to Refresh() ScottPlot's
// static layer (axis-label change). Other "accumulator" fields removed —
// nothing read them.
public sealed record HistogramFrame(
    double YMax,
    byte[] Buffer,
    int PixelWidth,
    int PixelHeight
) : ProcessedData(Buffer, PixelWidth, PixelHeight);
