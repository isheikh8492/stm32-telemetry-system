using System.Linq;
using ScottPlot;
using ScottPlot.TickGenerators;
using Telemetry.Viewer.Models.Plots;

namespace Telemetry.Viewer.Views.Plots;

// Tick-generator factories for binned plots whose X axis is rendered in
// "bin-position" space (0..BinCount). A single bin is one X-unit wide;
// labels translate bin positions back to data-space values per the chosen
// AxisScale. Inspired by the Worksheet repo's AxisFactory pattern.
internal static class AxisTicks
{
    private static readonly string[] _superscripts =
        { "⁰", "¹", "²", "³", "⁴", "⁵", "⁶", "⁷", "⁸", "⁹" };

    // Builds a NumericManual tick generator for the bin-position X axis.
    // Major ticks at "nice" data values for the chosen scale; minor ticks
    // subdivide between majors.
    public static NumericManual ForBinPositionX(double minRange, double maxRange, int binCount, AxisScale scale)
    {
        return scale switch
        {
            AxisScale.Logarithmic => BuildLogTicks(minRange, maxRange, binCount),
            _                     => BuildLinearTicks(minRange, maxRange, binCount),
        };
    }

    // Linear count axis (Y) — auto generator with SI-prefix labels.
    public static NumericAutomatic ForLinearY()
    {
        return new NumericAutomatic { LabelFormatter = FormatSiPrefix };
    }

    // ---- Linear X (bin-position space) ----

    private static NumericManual BuildLinearTicks(double minRange, double maxRange, int binCount)
    {
        // ~10 majors across the full range, ~4 minors between each.
        const int targetMajors = 10;
        var step = NiceStep((maxRange - minRange) / targetMajors);
        var minorStep = step / 5;

        var ticks = new System.Collections.Generic.List<Tick>();
        for (double v = NextMultiple(minRange, step); v <= maxRange; v += step)
        {
            var pos = (v - minRange) / (maxRange - minRange) * binCount;
            ticks.Add(new Tick(pos, v.ToString("0.##"), true));
        }
        for (double v = NextMultiple(minRange, minorStep); v <= maxRange; v += minorStep)
        {
            // Skip minors that coincide with majors.
            if (Math.Abs((v / step) - Math.Round(v / step)) < 1e-9) continue;
            var pos = (v - minRange) / (maxRange - minRange) * binCount;
            ticks.Add(new Tick(pos, "", false));
        }
        return new NumericManual(ticks.OrderBy(t => t.Position).ToArray());
    }

    // ---- Log X (bin-position space) ----

    private static NumericManual BuildLogTicks(double minRange, double maxRange, int binCount)
    {
        if (minRange <= 0 || maxRange <= minRange) return new NumericManual(Array.Empty<Tick>());

        var logMin = Math.Log10(minRange);
        var logMax = Math.Log10(maxRange);
        var binsPerLogUnit = binCount / (logMax - logMin);

        var ticks = new System.Collections.Generic.List<Tick>();

        var firstDecade = (int)Math.Ceiling(logMin);
        var lastDecade  = (int)Math.Floor(logMax);

        for (int decade = firstDecade; decade <= lastDecade; decade++)
        {
            var pos = (decade - logMin) * binsPerLogUnit;
            ticks.Add(new Tick(pos, FormatLogLabel(decade), true));

            for (int m = 2; m <= 9; m++)
            {
                var minorValue = m * Math.Pow(10, decade);
                if (minorValue < minRange || minorValue > maxRange) continue;
                var minorPos = (Math.Log10(minorValue) - logMin) * binsPerLogUnit;
                ticks.Add(new Tick(minorPos, "", false));
            }
        }

        return new NumericManual(ticks.OrderBy(t => t.Position).ToArray());
    }

    // ---- Formatters ----

    private static string FormatLogLabel(int exponent)
    {
        var sign = exponent < 0 ? "⁻" : "";
        var digits = Math.Abs(exponent).ToString();
        var sup = string.Concat(digits.Select(d => _superscripts[d - '0']));
        return $"10{sign}{sup}";
    }

    private static string FormatSiPrefix(double v)
    {
        var abs = Math.Abs(v);
        if (abs >= 1e9) return $"{v / 1e9:0.##}G";
        if (abs >= 1e6) return $"{v / 1e6:0.##}M";
        if (abs >= 1e3) return $"{v / 1e3:0.##}k";
        return v.ToString("0.##");
    }

    // Round a step up to a "nice" 1/2/5 × 10^k value.
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
}
