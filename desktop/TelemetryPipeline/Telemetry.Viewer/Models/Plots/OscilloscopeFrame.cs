namespace Telemetry.Viewer.Models.Plots;

public sealed record OscilloscopeFrame(
    uint EventId,
    byte[] Buffer,
    int PixelWidth,
    int PixelHeight
) : EventFrame(EventId, Buffer, PixelWidth, PixelHeight);
