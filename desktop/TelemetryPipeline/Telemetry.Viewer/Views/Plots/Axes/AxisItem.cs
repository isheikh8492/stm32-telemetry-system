using ScottPlot;
using ScottPlot.TickGenerators;
using Telemetry.Viewer.Models.Plots;

namespace Telemetry.Viewer.Views.Plots.Axes;

// Per-scale axis: bin math + tick layout. One file per scale type (Linear,
// Logarithmic, Biexponential). Works for any axis orientation (Bottom or
// Left) — ticks are produced in data space; the caller decides how to apply
// them to a specific WpfPlot axis.
//
// Trusts settings invariants — callers are responsible for ensuring
// MinRange < MaxRange and any scale-specific constraints (e.g. log requires
// MinRange > 0).
public abstract class AxisItem
{
    public abstract AxisScale Scale { get; }

    // Map a data value to its fractional bin position in [0, bins].
    // Used for both GetBinIndex (cast to int) and bin-space tick rendering.
    public abstract double DataValueToBinPosition(double value, double min, double max, int bins);

    // [start, end) edges of the given bin in data space.
    public abstract (double Start, double End) GetBinRange(int idx, double min, double max, int bins);

    // Major + minor ticks in data space across [min, max]. Subclasses choose
    // tick density and label formatting per scale.
    public abstract Tick[] ComputeTicks(double min, double max);

    // Convenience: integer bin index from a data value, or -1 if out of range.
    public int GetBinIndex(double value, double min, double max, int bins)
    {
        var pos = DataValueToBinPosition(value, min, max, bins);
        return (pos >= 0 && pos < bins) ? (int)pos : -1;
    }

    // Tick generator in DATA SPACE — for axes whose coordinates are data
    // values (e.g. histogram Y axis, raw-value X axes).
    public NumericManual BuildTickGenerator(double min, double max)
        => new(ComputeTicks(min, max));

    // Tick generator in BIN-POSITION SPACE (0..bins) — for histogram-style
    // X axes where bars sit at integer bin positions but axis labels show
    // data values.
    public NumericManual BuildBinTickGenerator(double min, double max, int bins)
    {
        var ticks = ComputeTicks(min, max);
        var binTicks = new Tick[ticks.Length];
        for (int i = 0; i < ticks.Length; i++)
        {
            var t = ticks[i];
            binTicks[i] = new Tick(
                DataValueToBinPosition(t.Position, min, max, bins),
                t.Label,
                t.IsMajor);
        }
        return new NumericManual(binTicks);
    }
}
