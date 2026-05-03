namespace Telemetry.Viewer.Models.Plots;

// How a histogram (or other binned plot) maps data values to bins along an
// axis. Drives both binning math (which bin a value lands in) and tick
// rendering (where major/minor labels appear).
public enum AxisScale
{
    Linear,
    Logarithmic,
    // Biexponential later — same enum slot keeps callers stable.
}
