using System.Windows;
using Telemetry.Viewer.Common;
using Telemetry.Viewer.Models;

namespace Telemetry.Viewer.Views.Worksheet;

// Owns "is the user currently arming a click-to-drop" state. Toolbar's
// Add-<X> button calls Arm(factory); next worksheet click calls TryPlace
// which (if armed) returns a fully built PlotPresenter at the snapped point
// and clears the arming. Cursor updates fire via IsArmed PropertyChanged so
// the View can flip the canvas cursor without code-behind logic.
public sealed class PlotPlacementController : ObservableObject
{
    private readonly PlotTypeRegistry _registry;
    private readonly Func<double> _getSnap;
    private Func<PlotSettings>? _pending;

    public PlotPlacementController(PlotTypeRegistry registry, Func<double> getSnap)
    {
        _registry = registry;
        _getSnap = getSnap;
    }

    public bool IsArmed => _pending is not null;

    public void Arm(Func<PlotSettings> factory)
    {
        _pending = factory;
        OnPropertyChanged(nameof(IsArmed));
    }

    public void Disarm()
    {
        if (_pending is null) return;
        _pending = null;
        OnPropertyChanged(nameof(IsArmed));
    }

    // Returns the new presenter if armed, else null. The next-z-index is
    // managed by the caller (Worksheet) since it knows the current top.
    public Models.Worksheet.PlotPresenter? TryPlace(Point at, int zIndex)
    {
        if (_pending is null) return null;

        var settings = _pending();
        Disarm();

        var snap = _getSnap();
        var size = _registry.DefaultSize(settings.Type);
        return new Models.Worksheet.PlotPresenter(
            settings,
            x:      Snap(at.X, snap),
            y:      Snap(at.Y, snap),
            width:  size.Width,
            height: size.Height,
            zIndex: zIndex);
    }

    private static double Snap(double v, double s) => s > 0 ? Math.Round(v / s) * s : v;
}
