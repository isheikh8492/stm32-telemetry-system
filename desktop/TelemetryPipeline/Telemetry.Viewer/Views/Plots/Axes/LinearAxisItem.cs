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

    // ~10 majors at 1/2/5×10ⁿ steps; 5 minors per major. SI-prefix labels.
    public override Tick[] ComputeTicks(double min, double max)
    {
        if (max <= min) return Array.Empty<Tick>();

        const int targetMajors = 10;
        var step = NiceStep((max - min) / targetMajors);
        var minorStep = step / 5;

        var ticks = new List<Tick>();
        for (double v = NextMultiple(min, step); v <= max + step * 1e-9; v += step)
            ticks.Add(new Tick(v, FormatSiPrefix(v), true));

        for (double v = NextMultiple(min, minorStep); v <= max + minorStep * 1e-9; v += minorStep)
        {
            if (Math.Abs((v / step) - Math.Round(v / step)) < 1e-9) continue;  // skip majors
            ticks.Add(new Tick(v, "", false));
        }

        ticks.Sort((a, b) => a.Position.CompareTo(b.Position));
        return ticks.ToArray();
    }

    // Round a step up to a "nice" 1/2/5 × 10ⁿ value.
    private static double NiceStep(double raw)
    {
        if (raw <= 0) return 1;
        var exp = Math.Floor(Math.Log10(raw));
        var pow = Math.Pow(10, exp);
        var frac = raw / pow;
        var nice = frac < 1.5 ? 1 : frac < 3.5 ? 2 : frac < 7.5 ? 5 : 10;
        return nice * pow;
    }

    private static double NextMultiple(double from, double step)
        => Math.Ceiling(from / step) * step;

    private static string FormatSiPrefix(double v)
    {
        var abs = Math.Abs(v);
        if (abs >= 1e9) return $"{v / 1e9:0.##}G";
        if (abs >= 1e6) return $"{v / 1e6:0.##}M";
        if (abs >= 1e3) return $"{v / 1e3:0.##}k";
        return v.ToString("0.##");
    }
}
