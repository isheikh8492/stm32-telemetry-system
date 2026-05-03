namespace Telemetry.Viewer.Models.Plots;

public sealed record OscilloscopeSettings(Guid PlotId, int ChannelId) : PlotSettings(PlotId);
