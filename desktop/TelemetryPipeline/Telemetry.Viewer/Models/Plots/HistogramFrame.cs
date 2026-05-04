namespace Telemetry.Viewer.Models.Plots;

// One bin of a 1D histogram. Self-describing: contains its own range and count
// so the renderer doesn't need to consult HistogramSettings to interpret the data.
//   [Start, End)  — left-closed, right-open interval (numpy convention)
//   Count         — number of events that fell in this bin
public readonly record struct HistogramBin(double Start, double End, long Count);

// Snapshot of an accumulating 1D histogram. Carries the pre-painted pixel
// buffer via the ProcessedData base. YMax is published so PlotItem can
// decide whether to Refresh() ScottPlot's static layer (axis-label change).
public sealed record HistogramFrame(
    long EventsAccumulated,
    IReadOnlyList<HistogramBin> Bins,
    long MaxCount,
    double YMax,
    byte[] Buffer,
    int PixelWidth,
    int PixelHeight
) : AnalysisFrame(EventsAccumulated, Buffer, PixelWidth, PixelHeight);
