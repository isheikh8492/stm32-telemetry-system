namespace Telemetry.Viewer.Models.Plots;

public sealed record OscilloscopeFrame(
    byte[] Buffer,
    int PixelWidth,
    int PixelHeight
) : ProcessedData(Buffer, PixelWidth, PixelHeight);
