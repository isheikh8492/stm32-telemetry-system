using System.Linq;
using ScottPlot;
using Telemetry.Viewer.Models.Plots;

namespace Telemetry.Viewer.Views.Plots.Axes;

public sealed class LogarithmicAxisItem : AxisItem
{
    private static readonly string[] _superscripts =
        { "⁰", "¹", "²", "³", "⁴", "⁵", "⁶", "⁷", "⁸", "⁹" };

    public override AxisScale Scale => AxisScale.Logarithmic;

    public override double DataValueToBinPosition(double value, double min, double max, int bins)
    {
        var logMin = Math.Log10(min);
        var logMax = Math.Log10(max);
        return (Math.Log10(value) - logMin) / (logMax - logMin) * bins;
    }

    public override (double Start, double End) GetBinRange(int idx, double min, double max, int bins)
    {
        var logMin = Math.Log10(min);
        var logMax = Math.Log10(max);
        var step = (logMax - logMin) / bins;
        return (Math.Pow(10, logMin + idx * step),
                Math.Pow(10, logMin + (idx + 1) * step));
    }

    // Major ticks at decade boundaries (10⁰, 10¹, …); minor ticks at 2x..9x.
    public override Tick[] ComputeTicks(double min, double max)
    {
        if (min <= 0 || max <= min) return Array.Empty<Tick>();

        var ticks = new List<Tick>();
        var firstDecade = (int)Math.Ceiling(Math.Log10(min));
        var lastDecade  = (int)Math.Floor(Math.Log10(max));

        for (int decade = firstDecade; decade <= lastDecade; decade++)
        {
            var dataValue = Math.Pow(10, decade);
            ticks.Add(new Tick(dataValue, FormatLogLabel(decade), true));

            for (int m = 2; m <= 9; m++)
            {
                var minorValue = m * Math.Pow(10, decade);
                if (minorValue < min || minorValue > max) continue;
                ticks.Add(new Tick(minorValue, "", false));
            }
        }

        ticks.Sort((a, b) => a.Position.CompareTo(b.Position));
        return ticks.ToArray();
    }

    private static string FormatLogLabel(int exponent)
    {
        var sign = exponent < 0 ? "⁻" : "";
        var digits = Math.Abs(exponent).ToString();
        var sup = string.Concat(digits.Select(d => _superscripts[d - '0']));
        return $"10{sign}{sup}";
    }
}
