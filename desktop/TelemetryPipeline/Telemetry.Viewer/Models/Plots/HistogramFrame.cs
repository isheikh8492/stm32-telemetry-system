namespace Telemetry.Viewer.Models.Plots;

// Snapshot of an accumulating 1D histogram. Carries the pre-painted pixel
// buffer via the ProcessedData base. YMax is published so PlotItem can
// decide whether to Refresh() ScottPlot's static layer (axis-label change).
public sealed record HistogramFrame(
    long EventsAccumulated,
    long MaxCount,
    double YMax,
    byte[] Buffer,
    int PixelWidth,
    int PixelHeight
) : AnalysisFrame(EventsAccumulated, Buffer, PixelWidth, PixelHeight);
