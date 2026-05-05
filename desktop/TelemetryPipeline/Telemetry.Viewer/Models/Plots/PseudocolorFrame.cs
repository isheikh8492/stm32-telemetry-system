namespace Telemetry.Viewer.Models.Plots;

public sealed record PseudocolorFrame(
    byte[] Buffer,
    int PixelWidth,
    int PixelHeight
) : ProcessedData(Buffer, PixelWidth, PixelHeight);
