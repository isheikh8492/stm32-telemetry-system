namespace Telemetry.Viewer.Models.Plots;

public sealed record SpectralRibbonFrame(
    long EventsAccumulated,
    long MaxCount,
    byte[] Buffer,
    int PixelWidth,
    int PixelHeight
) : AnalysisFrame(EventsAccumulated, Buffer, PixelWidth, PixelHeight);
