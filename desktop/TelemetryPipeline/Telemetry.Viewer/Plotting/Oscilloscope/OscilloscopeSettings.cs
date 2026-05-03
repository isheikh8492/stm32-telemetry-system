using Telemetry.Viewer.Plotting;

namespace Telemetry.Viewer.Plotting.Oscilloscope;

public sealed record OscilloscopeSettings(Guid PlotId, int ChannelId) : PlotSettings(PlotId);
