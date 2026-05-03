using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Worksheet;
using Telemetry.Viewer.Services.ContextMenu;
using Telemetry.Viewer.Services.DataSources;

namespace Telemetry.Viewer.Services.Pipeline;

public sealed class ViewportSession : IDisposable
{
    private static readonly TimeSpan DefaultProcessingInterval = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan DefaultRenderingInterval = TimeSpan.FromMilliseconds(33);

    private readonly DataStore _store = new();
    private readonly ProcessingEngine _processing;
    private readonly RenderingEngine _rendering;
    private readonly IContextMenuProvider? _menuProvider;

    public ViewportSession(
        IDataSource source,
        SynchronizationContext uiContext,
        TimeSpan? processingInterval = null,
        TimeSpan? renderingInterval = null,
        IContextMenuProvider? contextMenuProvider = null)
    {
        _processing = new ProcessingEngine(
            source, _store,
            processingInterval ?? DefaultProcessingInterval);

        _rendering = new RenderingEngine(
            uiContext, _store,
            renderingInterval ?? DefaultRenderingInterval);

        _menuProvider = contextMenuProvider;
    }

    public bool IsRunning => _processing.IsRunning && _rendering.IsRunning;

    public DataStore Store => _store;

    // ---- SettingsSink ----
    // The VM calls these when the worksheet changes so ProcessingEngine and
    // RenderingEngine stay in sync with what's actually on screen.

    public void AddPlot(IPlotView plotView)
    {
        _store.UpsertSettings(plotView.Settings);
        _rendering.Register(plotView.Settings.PlotId, plotView);

        if (_menuProvider is not null)
        {
            plotView.AttachContextMenu(() =>
            {
                var current = _store.GetSettings(plotView.Settings.PlotId) ?? plotView.Settings;
                return _menuProvider.GetMenuFor(current);
            });
        }
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
