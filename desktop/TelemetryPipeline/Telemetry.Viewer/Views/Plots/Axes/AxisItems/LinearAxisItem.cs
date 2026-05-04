using ScottPlot;
using Telemetry.Viewer.Models.Plots;

namespace Telemetry.Viewer.Views.Plots.Axes;

public sealed class LinearAxisItem : AxisItem
{
    public override AxisScale Scale => AxisScale.Linear;

    public override double DataValueToBinPosition(double value, double min, double max, int bins)
        => (value - min) / (max - min) * bins;

    public override (double Start, double End) GetBinRange(int idx, double min, double max, int bins)
    {
        var width = (max - min) / bins;
        return (min + idx * width, min + (idx + 1) * width);
    }

    // 3–6 majors at 1/2/5×10ⁿ steps, regardless of how "messy" min/max are.
    // 5 minors per major. SI-prefix labels.
    public override Tick[] ComputeTicks(double min, double max)
    {
        if (max <= min) return Array.Empty<Tick>();

        var step = ChooseStep(max - min);
        var minorStep = step / 5;

        var ticks = new List<Tick>();
        for (double v = NextMultiple(min, step); v <= max + step * 1e-9; v += step)
            ticks.Add(new Tick(v, AxisFormatters.NumericSiPrefix(v), true));

        for (double v = NextMultiple(min, minorStep); v <= max + minorStep * 1e-9; v += minorStep)
        {
            if (Math.Abs((v / step) - Math.Round(v / step)) < 1e-9) continue;  // skip majors
            ticks.Add(new Tick(v, "", false));
        }

        ticks.Sort((a, b) => a.Position.CompareTo(b.Position));
        return ticks.ToArray();
    }

    // Pick a 1/2/5×10ⁿ step that produces between 3 and 6 majors across the
    // range. Survives ugly min/max values (e.g. 37 → 8423) by sweeping nice
    // steps around the order of magnitude of range/5.
    private static readonly double[] _niceFractions = { 1, 2, 5 };
    private static double ChooseStep(double range)
    {
        const int minMajors = 3;
        const int maxMajors = 6;

        var exp = (int)Math.Floor(Math.Log10(range / 5));

        // Sweep small → large; first step that yields a count in [3, 6] wins.
        for (int e = exp - 1; e <= exp + 2; e++)
        {
            var pow = Math.Pow(10, e);
            foreach (var f in _niceFractions)
            {
                var step = f * pow;
                var count = (int)Math.Floor(range / step) + 1;
                if (count >= minMajors && count <= maxMajors) return step;
            }
        }

        // Fallback if the sweep finds nothing — give a sane step so the loop
        // doesn't divide by zero downstream.
        return Math.Pow(10, exp);
    }

    private static double NextMultiple(double from, double step)
        => Math.Ceiling(from / step) * step;
}
