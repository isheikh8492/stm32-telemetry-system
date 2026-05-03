namespace Telemetry.Viewer.Models.Plots;

// 1D histogram of a single channel's parameter, accumulated across events.
//   X axis = bins of the chosen ParamType in [MinRange, MaxRange]
//   Y axis = count
public sealed class HistogramSettings : PlotSettings
{
    private int _channelId;
    private ParamType _param;
    private int _binCount;
    private double _minRange;
    private double _maxRange;

    public HistogramSettings(
        Guid plotId,
        int channelId,
        ParamType param,
        int binCount,
        double minRange,
        double maxRange) : base(plotId)
    {
        _channelId = channelId;
        _param = param;
        _binCount = binCount;
        _minRange = minRange;
        _maxRange = maxRange;
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

    public ParamType Param   { get => _param;    set => SetProperty(ref _param, value); }
    public int BinCount      { get => _binCount; set => SetProperty(ref _binCount, value); }
    public double MinRange   { get => _minRange; set => SetProperty(ref _minRange, value); }
    public double MaxRange   { get => _maxRange; set => SetProperty(ref _maxRange, value); }

    public override string DisplayName => $"Histogram (ch {_channelId})";
}
