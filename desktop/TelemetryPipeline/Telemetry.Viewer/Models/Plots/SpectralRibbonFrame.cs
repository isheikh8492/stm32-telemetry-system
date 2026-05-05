namespace Telemetry.Viewer.Models.Plots;

public sealed record SpectralRibbonFrame(
    byte[] Buffer,
    int PixelWidth,
    int PixelHeight
) : ProcessedData(Buffer, PixelWidth, PixelHeight);
