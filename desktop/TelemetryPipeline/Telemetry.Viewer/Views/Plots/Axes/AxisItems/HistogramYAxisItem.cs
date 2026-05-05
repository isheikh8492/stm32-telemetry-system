using ScottPlot;

namespace Telemetry.Viewer.Views.Plots.Axes;

// The histogram's Y (count) axis: always 6 majors evenly spaced across the
// current range, with the first locked at 0 and the other 5 re-labeled as
// the range grows. Built on top of FixedCountTickGenerator — installed once
// in the plot's static layer; the plot view moves SetLimitsY each render,
// ScottPlot calls Regenerate, and labels follow. No axis-layout work per
// render.
//
// Labels use AxisFormatters.CountSiPrefix (integer below 1K; K / M / B
// thereafter). To make every label a "nice" number, the plot uses NiceMax
// to round its yMax up so each fifth of the range is a 1/2/5×10ⁿ step.
public static class HistogramYAxisItem
{
    private const int Divisions = 5;        // 6 majors = 5 intervals
    private const double FloorYMax = 10;    // start at 10; expand from there
    private const double Padding = 1.1;     // 10% headroom above maxCount

    public static ITickGenerator CreateTickGenerator()
        => new FixedCountTickGenerator(majorCount: Divisions + 1, format: AxisFormatters.CountSiPrefix);

    // Smart Y limit for the histogram:
    //   * Floors at 1K so an empty / barely-populated histogram still shows
    //     a usable count axis instead of [0, 0].
    //   * Pads maxCount by 10% so the tallest bar never touches the top tick.
    //   * Rounds the result up so (NiceMax / 5) is a 1/2/5×10ⁿ step —
    //     guarantees all 6 tick positions are clean numbers.
    public static double NiceMax(double maxCount)
    {
        if (maxCount <= 0) return FloorYMax;

        var padded = maxCount * Padding;
        if (padded <= FloorYMax) return FloorYMax;

        var step = NiceStep(padded / Divisions);
        return step * Divisions;
    }

    // Smallest 1/2/5×10ⁿ value that is ≥ raw. Rounds UP — never returns a
    // value smaller than the input. (The "round to nearest" variant let
    // raw=1100 collapse to 1000, which made yMax=1000 and bars-at-1000 sit
    // exactly at the top of the axis.)
    private static double NiceStep(double raw)
    {
        var exp = Math.Floor(Math.Log10(raw));
        var pow = Math.Pow(10, exp);
        var frac = raw / pow;
        var nice = frac <= 1 ? 1 : frac <= 2 ? 2 : frac <= 5 ? 5 : 10;
        return nice * pow;
    }
}
