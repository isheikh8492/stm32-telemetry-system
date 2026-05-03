namespace Telemetry.Viewer.Models.Plots;

public sealed record OscilloscopeFrame(uint EventId, IReadOnlyList<ushort> Samples)
    : EventFrame(EventId);
