using Telemetry.Viewer.Services.DataSources;

namespace Telemetry.Viewer.Services.Pipeline;

public sealed class ViewportSession : IDisposable
{
    private static readonly TimeSpan DefaultProcessingInterval = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan DefaultRenderingInterval = TimeSpan.FromMilliseconds(33);

    private readonly DataStore _store = new();
    private readonly ProcessingEngine _processing;
    private readonly RenderingEngine _rendering;

    // Per-plot teardown action. Captured at AddPlot time, invoked at RemovePlot
    // (or Dispose) so we don't keep stale plot items alive via event handlers.
    private readonly Dictionary<Guid, Action> _teardown = new();

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
    // The session is the single owner of every channel that flows into the
    // DataStore (settings + pixel size). Callers never poke the store directly.
    // Pixel size is hydrated from the plot's current PixelWidth/Height (handles
    // plots added before the session existed) and kept in sync via DataAreaChanged.

    public void AddPlot(IRenderTarget target)
    {
        var id = target.Id;
        _store.UpsertSettings(target.Settings);
        _rendering.Register(id, target);

        PushPixelSize(target);
        Action<System.Windows.Rect> listener = _ => PushPixelSize(target);
        target.DataAreaChanged += listener;
        _teardown[id] = () => target.DataAreaChanged -= listener;
    }

    public void RemovePlot(Guid plotId)
    {
        if (_teardown.Remove(plotId, out var teardown))
            teardown();
        _store.RemovePlot(plotId);
        _rendering.Unregister(plotId);
    }

    private void PushPixelSize(IRenderTarget target)
    {
        if (target.PixelWidth > 0 && target.PixelHeight > 0)
            _store.UpsertPixelSize(target.Id, target.PixelWidth, target.PixelHeight);
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
        foreach (var teardown in _teardown.Values) teardown();
        _teardown.Clear();
        _processing.Dispose();
        _rendering.Dispose();
    }
}
