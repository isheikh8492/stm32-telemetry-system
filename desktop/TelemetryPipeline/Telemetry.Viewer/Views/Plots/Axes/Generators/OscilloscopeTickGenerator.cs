using ScottPlot;
using ScottPlot.TickGenerators;

namespace Telemetry.Viewer.Views.Plots.Axes;

// Oscilloscope-specific axis configuration. Owns the constants (ADC range,
// sample count, major/minor steps) and exposes precomputed tick generators
// for each axis. Plot view stays clean — just calls BuildY()/BuildX().
public static class OscilloscopeTickGenerator
{
    // Y axis: 0–5000 ADC, majors at 1000 (with N0 labels), minors at 200.
    public static NumericManual BuildY()
    {
        var ticks = new List<Tick>();
        for (double v = 0; v <= 5000; v += 200)
        {
            bool isMajor = ((int)v) % 1000 == 0;
            ticks.Add(new Tick(v, isMajor ? v.ToString("N0") : "", isMajor));
        }
        return new NumericManual(ticks.ToArray());
    }

    // X axis: 0–32 samples, majors every 4.
    public static NumericFixedInterval BuildX() => new(4);
}
