namespace Telemetry.Viewer.Models.Plots;

// 1D histogram of a single channel's parameter, accumulated across events.
//   X axis = bins of the chosen ParamType in [MinRange, MaxRange]
//   Y axis = count
public sealed record HistogramSettings(
    Guid PlotId,
    int ChannelId,
    ParamType Param,
    int BinCount,
    double MinRange,
    double MaxRange
) : PlotSettings(PlotId);
