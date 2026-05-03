namespace Telemetry.Viewer.Models.Plots;

// One bin of a 1D histogram. Self-describing: contains its own range and count
// so the renderer doesn't need to consult HistogramSettings to interpret the data.
//   [Start, End)  — left-closed, right-open interval (numpy convention)
//   Count         — number of events that fell in this bin
// Declared as a record struct: no per-bin heap allocation, just an inline value.
public readonly record struct HistogramBin(double Start, double End, long Count);

// Snapshot of an accumulating 1D histogram. The bin list is rebuilt on each
// snapshot from the running count array; bin edges come from settings and are
// stamped into each HistogramBin so consumers don't cross-reference settings.
public sealed record HistogramFrame(
    long EventsAccumulated,             // total events counted into the histogram
    IReadOnlyList<HistogramBin> Bins
) : AnalysisFrame(EventsAccumulated);
