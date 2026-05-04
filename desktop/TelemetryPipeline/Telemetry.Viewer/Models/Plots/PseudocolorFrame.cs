namespace Telemetry.Viewer.Models.Plots;

// Snapshot of an accumulating 2D histogram, painted as a hot-colormap heatmap.
//   MaxCount  — peak bin count, used for normalization at paint time.
//   Buffer    — pre-painted Pbgra32 pixel buffer (count==0 cells transparent).
public sealed record PseudocolorFrame(
    long EventsAccumulated,
    long MaxCount,
    byte[] Buffer,
    int PixelWidth,
    int PixelHeight
) : AnalysisFrame(EventsAccumulated, Buffer, PixelWidth, PixelHeight);
