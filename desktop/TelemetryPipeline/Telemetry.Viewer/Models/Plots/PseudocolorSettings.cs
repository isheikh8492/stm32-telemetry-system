using Telemetry.Viewer.Models;

namespace Telemetry.Viewer.Models.Plots;

// 2D histogram of two parameter selections, rendered as a heatmap.
//   X axis = bins of (XChannelId, XParam) under XScale, in [XMinRange, XMaxRange].
//   Y axis = bins of (YChannelId, YParam) under YScale, in [YMinRange, YMaxRange].
//   Color  = count, hot-colormap (black→red→yellow→white).
//
// Settings are pure data; bin math + color mapping live in the processor.
// Selections are exposed as SelectionStrategy instances so the dialog and
// processor can read channel name/color via the same seam histograms use.
public sealed class PseudocolorSettings : PlotSettings
{
    private int _xChannelId; private ParamType _xParam;
    private int _yChannelId; private ParamType _yParam;
    private BinCount _binCount;
    private double _xMinRange, _xMaxRange;
    private double _yMinRange, _yMaxRange;
    private AxisScale _xScale, _yScale;

    public PseudocolorSettings(
        Guid plotId,
        int xChannelId, ParamType xParam,
        int yChannelId, ParamType yParam,
        BinCount binCount,
        double xMinRange, double xMaxRange,
        double yMinRange, double yMaxRange,
        AxisScale xScale, AxisScale yScale) : base(plotId)
    {
        _xChannelId = xChannelId; _xParam = xParam;
        _yChannelId = yChannelId; _yParam = yParam;
        _binCount = binCount;
        _xMinRange = xMinRange; _xMaxRange = xMaxRange;
        _yMinRange = yMinRange; _yMaxRange = yMaxRange;
        _xScale = xScale; _yScale = yScale;
    }

    public int       XChannelId { get => _xChannelId; set { if (SetProperty(ref _xChannelId, value)) OnPropertyChanged(nameof(DisplayName)); } }
    public ParamType XParam     { get => _xParam;     set { if (SetProperty(ref _xParam,     value)) OnPropertyChanged(nameof(DisplayName)); } }
    public int       YChannelId { get => _yChannelId; set { if (SetProperty(ref _yChannelId, value)) OnPropertyChanged(nameof(DisplayName)); } }
    public ParamType YParam     { get => _yParam;     set { if (SetProperty(ref _yParam,     value)) OnPropertyChanged(nameof(DisplayName)); } }
    public BinCount  BinCount   { get => _binCount;   set => SetProperty(ref _binCount, value); }
    public double    XMinRange  { get => _xMinRange;  set => SetProperty(ref _xMinRange, value); }
    public double    XMaxRange  { get => _xMaxRange;  set => SetProperty(ref _xMaxRange, value); }
    public double    YMinRange  { get => _yMinRange;  set => SetProperty(ref _yMinRange, value); }
    public double    YMaxRange  { get => _yMaxRange;  set => SetProperty(ref _yMaxRange, value); }
    public AxisScale XScale     { get => _xScale;     set => SetProperty(ref _xScale, value); }
    public AxisScale YScale     { get => _yScale;     set => SetProperty(ref _yScale, value); }

    public override PlotType Type => PlotType.Pseudocolor;
    public override string DisplayName => $"Pseudocolor ({XSelection.Channel.Name} {_xParam} × {YSelection.Channel.Name} {_yParam})";

    public SelectionStrategy XSelection => new(_xChannelId, _xParam);
    public SelectionStrategy YSelection => new(_yChannelId, _yParam);
}
