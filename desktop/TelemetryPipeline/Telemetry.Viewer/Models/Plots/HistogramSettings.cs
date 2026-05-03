using Telemetry.Viewer.Models;

namespace Telemetry.Viewer.Models.Plots;

// 1D histogram of a single channel's parameter, accumulated across events.
//   X axis = bins of Param in [MinRange, MaxRange] under the chosen Scale.
//   Y axis = count.
//
// Settings are pure data. Bin math lives in AxisFactory; per-event
// extraction lives in SelectionStrategy. Invariants (BinCount > 0,
// MinRange < MaxRange, log-scale ⇒ MinRange > 0) are upheld at the
// Properties dialog and at construction — settings does not defend.
public sealed class HistogramSettings : PlotSettings
{
    private int _channelId;
    private ParamType _param;
    private BinCount _binCount;
    private double _minRange;
    private double _maxRange;
    private AxisScale _scale;

    public HistogramSettings(
        Guid plotId,
        int channelId,
        ParamType param,
        BinCount binCount,
        double minRange,
        double maxRange,
        AxisScale scale = AxisScale.Linear) : base(plotId)
    {
        _channelId = channelId;
        _param = param;
        _binCount = binCount;
        _minRange = minRange;
        _maxRange = maxRange;
        _scale = scale;
    }

    public int ChannelId
    {
        get => _channelId;
        set
        {
            if (SetProperty(ref _channelId, value))
                OnPropertyChanged(nameof(DisplayName));
        }
    }

    public ParamType Param              { get => _param;    set => SetProperty(ref _param, value); }
    public BinCount BinCount   { get => _binCount; set => SetProperty(ref _binCount, value); }
    public double MinRange              { get => _minRange; set => SetProperty(ref _minRange, value); }
    public double MaxRange              { get => _maxRange; set => SetProperty(ref _maxRange, value); }
    public AxisScale Scale              { get => _scale;    set => SetProperty(ref _scale, value); }

    public override PlotType Type => PlotType.Histogram;
    public override string DisplayName => $"Histogram (ch {_channelId})";

    // Derived view: convert ChannelId + Param into the extraction strategy
    // ProcessingEngine consumes per snapshot.
    public SelectionStrategy Selection => new(_channelId, _param);
}
