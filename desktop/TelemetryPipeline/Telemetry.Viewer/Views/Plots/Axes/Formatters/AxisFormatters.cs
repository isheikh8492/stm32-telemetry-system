namespace Telemetry.Viewer.Views.Plots.Axes;

// Tick-label formatters shared across axis items and tick generators.
public static class AxisFormatters
{
    // For COUNT axes (histograms, event counters):
    //   < 1,000        → integer ("0", "200", "999")
    //   < 1,000,000    → K with at most one decimal ("1K", "2.5K", "10K", "100K")
    //   < 1,000,000,000→ M ("1M", "1.5M")
    //   ≥ 1B           → B ("1B", "2.3B")
    // No floats below 1K, no trailing-zero decimals (1.0K → "1K").
    public static string CountSiPrefix(double v)
    {
        var abs = Math.Abs(v);
        if (abs >= 1e9) return $"{v / 1e9:0.#}B";
        if (abs >= 1e6) return $"{v / 1e6:0.#}M";
        if (abs >= 1e3) return $"{v / 1e3:0.#}K";
        return ((long)Math.Round(v)).ToString();
    }
}
