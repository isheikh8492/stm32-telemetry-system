using Telemetry.Viewer.Models;

namespace Telemetry.Viewer.Models.Plots;

// 1D histogram of a single channel's parameter, accumulated across events.
//   X axis = bins of the chosen ParamType in [MinRange, MaxRange] under
//            the chosen Scale (Linear or Logarithmic).
//   Y axis = count
public sealed class HistogramSettings : PlotSettings
{
    private int _channelId;
    private ParamType _param;
    private int _binCount;
    private double _minRange;
    private double _maxRange;
    private AxisScale _scale;

    public HistogramSettings(
        Guid plotId,
        int channelId,
        ParamType param,
        int binCount,
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

    public ParamType Param   { get => _param;    set => SetProperty(ref _param, value); }
    public int BinCount      { get => _binCount; set => SetProperty(ref _binCount, value); }
    public double MinRange   { get => _minRange; set => SetProperty(ref _minRange, value); }
    public double MaxRange   { get => _maxRange; set => SetProperty(ref _maxRange, value); }
    public AxisScale Scale   { get => _scale;    set => SetProperty(ref _scale, value); }

    public override PlotType Type => PlotType.Histogram;
    public override string DisplayName => $"Histogram (ch {_channelId})";

    // Returns the bin index for a raw value, or null if it's out of range
    // or the configuration is invalid for the chosen scale.
    public int? GetBinIndex(double value)
    {
        if (BinCount <= 0 || MaxRange <= MinRange) return null;
        if (value < MinRange || value >= MaxRange) return null;

        if (Scale == AxisScale.Logarithmic)
        {
            if (MinRange <= 0 || value <= 0) return null;
            var logMin = Math.Log10(MinRange);
            var logMax = Math.Log10(MaxRange);
            var binWidth = (logMax - logMin) / BinCount;
            var idx = (int)((Math.Log10(value) - logMin) / binWidth);
            return idx >= 0 && idx < BinCount ? idx : null;
        }

        // Linear (and biexp fallback for now)
        var width = (MaxRange - MinRange) / BinCount;
        var i = (int)((value - MinRange) / width);
        return i >= 0 && i < BinCount ? i : null;
    }

    // Returns the [start, end) data-space edges of the given bin index.
    public (double Start, double End) GetBinRange(int binIndex)
    {
        if (Scale == AxisScale.Logarithmic && MinRange > 0 && MaxRange > MinRange)
        {
            var logMin = Math.Log10(MinRange);
            var logMax = Math.Log10(MaxRange);
            var step = (logMax - logMin) / BinCount;
            return (Math.Pow(10, logMin + binIndex * step),
                    Math.Pow(10, logMin + (binIndex + 1) * step));
        }

        var width = (MaxRange - MinRange) / BinCount;
        return (MinRange + binIndex * width, MinRange + (binIndex + 1) * width);
    }
}
