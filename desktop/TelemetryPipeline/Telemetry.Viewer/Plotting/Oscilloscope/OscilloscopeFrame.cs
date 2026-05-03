using Telemetry.Viewer.Plotting;

namespace Telemetry.Viewer.Plotting.Oscilloscope;

public sealed record OscilloscopeFrame(uint EventId, IReadOnlyList<ushort> Samples)
    : EventFrame(EventId);
