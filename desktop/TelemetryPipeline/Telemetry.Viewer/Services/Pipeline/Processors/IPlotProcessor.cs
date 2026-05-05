using Telemetry.Viewer.Models;
using Telemetry.Viewer.Services.DataSources;

namespace Telemetry.Viewer.Services.Pipeline.Processors;

// Per-plot-type compute + paint contract. Implementations:
//   1. read the data source,
//   2. compute whatever the plot needs (samples, bin counts, ...),
//   3. paint a Pbgra32 pixel buffer at the supplied target size,
//   4. return a ProcessedData record carrying the buffer + dims.
//
// Runs entirely on the ProcessingEngine's worker thread — must NOT touch
// any UI element.
public interface IPlotProcessor
{
    ProcessedData? Process(PlotSettings settings, IDataSource source, int pixelWidth, int pixelHeight);

    // Drop any per-plot incremental state the processor is caching. Called
    // when a plot is removed from the worksheet so we don't leak memory.
    // Default no-op for stateless processors.
    void ForgetState(Guid plotId) { }
}
