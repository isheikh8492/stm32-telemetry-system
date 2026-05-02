namespace Telemetry.Engine;

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

    public void Register(PlotSettings settings, IRenderTarget target)
    {
        _store.UpsertSettings(settings);
        _rendering.Register(settings.PlotId, target);
    }

    public void UpdateSettings(PlotSettings settings)
    {
        _store.UpsertSettings(settings);
        // ProcessingEngine sees a different fingerprint next tick -> reprocesses this plot.
    }

    public void Unregister(Guid plotId)
    {
        _store.RemovePlot(plotId);
        _rendering.Unregister(plotId);
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
