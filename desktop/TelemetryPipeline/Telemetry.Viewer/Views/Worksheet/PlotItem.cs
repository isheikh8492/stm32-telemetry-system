using System.Windows;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Worksheet;

namespace Telemetry.Viewer.Views.Worksheet;

// One plot's worth of state the Worksheet tracks: settings (the model the
// pipeline reads), view (what renders it), container (its visual wrapper),
// and the latest broadcast data-area rect — so DragHandler can snap the
// data rect's TL (not the outer's TL) to grid intersections during move.
internal sealed class PlotItem
{
    public PlotSettings Settings { get; }
    public IPlotView View { get; }
    public PlotContainer Container { get; }
    public Rect DataArea { get; set; }

    public PlotItem(PlotSettings settings, IPlotView view, PlotContainer container)
    {
        Settings = settings;
        View = view;
        Container = container;
    }
}
