using Telemetry.Viewer.Models;

namespace Telemetry.Viewer.Services.Pipeline;

public sealed class DataStore
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, PlotSettings> _settings = new();
    private readonly Dictionary<Guid, ProcessedData> _processed = new();
    // Per-plot pixel-buffer target size. Updated from the worksheet whenever
    // a plot's data rect changes; read by PlotProcessor when allocating the
    // painted byte[]. Bumps the settings.Version so the processor refingerprints.
    private readonly Dictionary<Guid, (int W, int H)> _pixelSizes = new();

    public void UpsertSettings(PlotSettings settings)
    {
        lock (_lock)
            _settings[settings.PlotId] = settings;
    }

    public void RemovePlot(Guid plotId)
    {
        lock (_lock)
        {
            _settings.Remove(plotId);
            _processed.Remove(plotId);
            _pixelSizes.Remove(plotId);
        }
    }

    public void UpsertPixelSize(Guid plotId, int width, int height)
    {
        lock (_lock)
            _pixelSizes[plotId] = (width, height);
    }

    public (int W, int H) GetPixelSize(Guid plotId)
    {
        lock (_lock)
            return _pixelSizes.TryGetValue(plotId, out var s) ? s : (0, 0);
    }

    public IReadOnlyList<PlotSettings> GetAllSettings()
    {
        lock (_lock)
            return _settings.Values.ToList();
    }

    public PlotSettings? GetSettings(Guid plotId)
    {
        lock (_lock)
            return _settings.TryGetValue(plotId, out var s) ? s : null;
    }

    public void SetProcessed(Guid plotId, ProcessedData data)
    {
        lock (_lock)
            _processed[plotId] = data;
    }

    public ProcessedData? GetProcessed(Guid plotId)
    {
        lock (_lock)
            return _processed.TryGetValue(plotId, out var d) ? d : null;
    }
}
