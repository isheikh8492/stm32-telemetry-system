namespace Telemetry.Engine;

public sealed record OscilloscopeSettings(Guid PlotId, int ChannelId) : PlotSettings(PlotId);
