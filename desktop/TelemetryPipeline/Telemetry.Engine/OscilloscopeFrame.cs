namespace Telemetry.Engine;

public sealed record OscilloscopeFrame(uint EventId, IReadOnlyList<ushort> Samples)
    : ProcessedData(EventId);
