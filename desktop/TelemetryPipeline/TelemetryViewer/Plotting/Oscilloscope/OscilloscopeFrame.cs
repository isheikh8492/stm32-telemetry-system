namespace TelemetryViewer;

public sealed record OscilloscopeFrame(uint EventId, IReadOnlyList<ushort> Samples)
    : EventFrame(EventId);
