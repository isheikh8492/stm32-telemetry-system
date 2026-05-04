using Telemetry.Viewer.Models.Plots;

namespace Telemetry.Viewer.Services.Pipeline.Processors;

// Static lookup: PlotType → IPlotProcessor. Each plot type's wiring file
// (<Type>Plot.cs) registers its processor at startup, the same way it
// registers its view factory + menu with the worksheet. ProcessingEngine
// reads via For(); it has no per-type knowledge.
public static class PlotProcessorRegistry
{
    private static readonly Dictionary<PlotType, IPlotProcessor> _processors = new();

    public static void Register(PlotType type, IPlotProcessor processor)
        => _processors[type] = processor;

    public static IPlotProcessor? For(PlotType type)
        => _processors.TryGetValue(type, out var p) ? p : null;
}
