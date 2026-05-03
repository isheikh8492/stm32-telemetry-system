using Telemetry.Viewer.Models;
using Telemetry.Viewer.Services.DataSources;
using Telemetry.Viewer.Views.Worksheet;

namespace Telemetry.Viewer.Services.Pipeline;

public sealed class ViewportSession : IDisposable
{
    private static readonly TimeSpan DefaultProcessingInterval = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan DefaultRenderingInterval = TimeSpan.FromMilliseconds(33);

    private readonly DataStore _store = new();
    private readonly ProcessingEngine _processing;
    private readonly RenderingEngine _rendering;

    public ViewportSession(
        IDataSource source,
        SynchronizationContext uiContext,
        TimeSpan? processingInterval = null,
        TimeSpan? renderingInterval = null)
    {
        _processing = new ProcessingEngine(
            source, _store,
            processingInterval ?? DefaultProcessingInterval);

        _rendering = new RenderingEngine(
            uiContext, _store,
            renderingInterval ?? DefaultRenderingInterval);
    }

    public bool IsRunning => _processing.IsRunning && _rendering.IsRunning;

    public DataStore Store => _store;

    // ---- SettingsSink ----
    // The VM calls these when the worksheet changes so ProcessingEngine and
    // RenderingEngine stay in sync with what's actually on screen.

    public void AddPlot(PlotItem plotItem)
    {
        _store.UpsertSettings(plotItem.Settings);
        _rendering.Register(plotItem.Settings.PlotId, plotItem);
    }

    public void RemovePlot(Guid plotId)
    {
        _store.RemovePlot(plotId);
        _rendering.Unregister(plotId);
    }

    public IReadOnlyDictionary<Type, double> GetProcessingTimes() =>
        _processing.GetAverageComputeTimes();

    public IReadOnlyDictionary<Type, double> GetRenderingTimes() =>
        _rendering.GetAverageRenderTimes();

    public void ResetMetrics()
    {
        _processing.ResetMetrics();
        _rendering.ResetMetrics();
    }

    public void Start()
    {
        _processing.Start();
        _rendering.Start();
    }

    public void Stop()
    {
        _processing.Stop();
        _rendering.Stop();
    }

    public void Dispose()
    {
        _processing.Dispose();
        _rendering.Dispose();
    }
}
