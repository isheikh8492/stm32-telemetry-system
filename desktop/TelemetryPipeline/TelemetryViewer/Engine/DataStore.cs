namespace TelemetryViewer;

public sealed class DataStore
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, PlotSettings> _settings = new();
    private readonly Dictionary<Guid, ProcessedData> _processed = new();

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
        }
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
