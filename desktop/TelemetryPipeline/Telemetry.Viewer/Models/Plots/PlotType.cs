namespace Telemetry.Viewer.Models.Plots;

// Stable identifier for each plot kind. Used for:
//   * processing dispatch (ProcessingEngine switches on this)
//   * worksheet registries (per-type factories, sizes, menu builders)
//   * future serialization (string discriminator survives type renames)
public enum PlotType
{
    Oscilloscope,
    Histogram,
    Pseudocolor,
}
