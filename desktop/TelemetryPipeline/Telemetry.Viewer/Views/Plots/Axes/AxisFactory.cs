using Telemetry.Viewer.Models.Plots;

namespace Telemetry.Viewer.Views.Plots.Axes;

// Static lookup of AxisItem instances by AxisScale. One singleton per scale
// type — axis items are stateless, so a single shared instance is fine.
public static class AxisFactory
{
    public static readonly LinearAxisItem       Linear       = new();
    public static readonly LogarithmicAxisItem  Logarithmic  = new();

    public static AxisItem For(AxisScale scale) => scale switch
    {
        AxisScale.Linear        => Linear,
        AxisScale.Logarithmic   => Logarithmic,
        _ => throw new NotSupportedException($"AxisScale {scale} has no axis item registered.")
    };
}
