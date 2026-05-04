using Telemetry.Viewer.Models;

namespace Telemetry.Viewer.Models.Plots;

// One column per selected channel; Y is a parameter-value histogram per
// channel; color = Turbo(count). Reads like "all channels' spectra
// side-by-side" — useful for spotting a detector that's drifted vs the
// rest. Param + Y range / scale / bin count are global (apply to every
// channel column).
public sealed class SpectralRibbonSettings : PlotSettings
{
    private IReadOnlyList<int> _channelIds;
    private ParamType _param;
    private BinCount _binCount;
    private double _minRange, _maxRange;
    private AxisScale _scale;

    public SpectralRibbonSettings(
        Guid plotId,
        IReadOnlyList<int> channelIds,
        ParamType param,
        BinCount binCount,
        double minRange,
        double maxRange,
        AxisScale scale = AxisScale.Linear) : base(plotId)
    {
        _channelIds = channelIds;
        _param = param;
        _binCount = binCount;
        _minRange = minRange;
        _maxRange = maxRange;
        _scale = scale;
    }

    public IReadOnlyList<int> ChannelIds
    {
        get => _channelIds;
        set
        {
            if (SetProperty(ref _channelIds, value))
                OnPropertyChanged(nameof(DisplayName));
        }
    }

    public ParamType Param      { get => _param;     set { if (SetProperty(ref _param, value)) OnPropertyChanged(nameof(DisplayName)); } }
    public BinCount  BinCount   { get => _binCount;  set => SetProperty(ref _binCount, value); }
    public double    MinRange   { get => _minRange;  set => SetProperty(ref _minRange, value); }
    public double    MaxRange   { get => _maxRange;  set => SetProperty(ref _maxRange, value); }
    public AxisScale Scale      { get => _scale;     set => SetProperty(ref _scale, value); }

    public override PlotType Type => PlotType.SpectralRibbon;
    public override string DisplayName => $"Spectral Ribbon ({_channelIds.Count} ch, {_param})";
}
