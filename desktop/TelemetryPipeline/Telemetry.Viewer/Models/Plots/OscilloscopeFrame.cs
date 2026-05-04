namespace Telemetry.Viewer.Models.Plots;

public sealed record OscilloscopeFrame(
    uint EventId,
    IReadOnlyList<ushort> Samples,
    byte[] Buffer,
    int PixelWidth,
    int PixelHeight
) : EventFrame(EventId, Buffer, PixelWidth, PixelHeight);
