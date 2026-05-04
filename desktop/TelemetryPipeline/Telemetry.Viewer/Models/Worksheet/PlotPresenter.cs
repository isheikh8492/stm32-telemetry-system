using Telemetry.Viewer.Common;

namespace Telemetry.Viewer.Models.Worksheet;

// Per-plot view state on the worksheet. Holds the plot's data settings plus
// the layout/position state that's specific to "where this plot lives on
// the canvas" — kept separate from PlotSettings so settings stays pure data.
//
// Bound to ContentPresenter via ItemsControl.ItemContainerStyle:
// Canvas.Left/Top/Panel.ZIndex follow X/Y/ZIndex; PlotItemHost reads
// Width/Height directly. IsSelected toggles the resize-thumb visibility.
public sealed class PlotPresenter : ObservableObject, IWorksheetItem
{
    public PlotSettings Settings { get; }

    private double _x, _y, _width, _height;
    private int _zIndex;
    private bool _isSelected;

    public PlotPresenter(PlotSettings settings, double x, double y, double width, double height, int zIndex)
    {
        Settings = settings;
        _x = x; _y = y; _width = width; _height = height; _zIndex = zIndex;
    }

    public Guid Id => Settings.PlotId;
    public string Name => Settings.DisplayName;

    public double X      { get => _x;      set => SetProperty(ref _x, value); }
    public double Y      { get => _y;      set => SetProperty(ref _y, value); }
    public double Width  { get => _width;  set => SetProperty(ref _width, value); }
    public double Height { get => _height; set => SetProperty(ref _height, value); }
    public int    ZIndex { get => _zIndex; set => SetProperty(ref _zIndex, value); }
    public bool   IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}
