namespace Telemetry.Viewer.Views.Plots.Axes;

// Tick-label formatters shared across axis items and tick generators.
// Suffix convention is canonical K/M/B/T uppercase across the app —
// don't drift to k/M/G or other variants.
public static class AxisFormatters
{
    // For COUNT axes (histograms, event counters):
    //   < 1K  → integer       ("0", "200", "999")
    //   < 1M  → "K"           ("1K", "2.5K", "10K", "100K")
    //   < 1B  → "M"           ("1M", "1.5M")
    //   < 1T  → "B"           ("1B", "2.3B")
    //   ≥ 1T  → "T"           ("1T", "1.5T")
    public static string CountSiPrefix(double v)
    {
        var abs = Math.Abs(v);
        if (abs >= 1e12) return $"{v / 1e12:0.#}T";
        if (abs >= 1e9)  return $"{v / 1e9:0.#}B";
        if (abs >= 1e6)  return $"{v / 1e6:0.#}M";
        if (abs >= 1e3)  return $"{v / 1e3:0.#}K";
        return ((long)Math.Round(v)).ToString();
    }

    // For NUMERIC axes (linear value labels). Same suffix convention as
    // CountSiPrefix above 1K, but allows up to 2 decimals below 1K so axes
    // with small-range (e.g. 0–1, 0–10) still show meaningful labels.
    public static string NumericSiPrefix(double v)
    {
        var abs = Math.Abs(v);
        if (abs >= 1e12) return $"{v / 1e12:0.##}T";
        if (abs >= 1e9)  return $"{v / 1e9:0.##}B";
        if (abs >= 1e6)  return $"{v / 1e6:0.##}M";
        if (abs >= 1e3)  return $"{v / 1e3:0.##}K";
        return v.ToString("0.##");
    }
}
