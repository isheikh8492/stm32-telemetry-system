using System.Windows.Threading;
using Telemetry.Viewer.Common;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.Pipeline;

namespace Telemetry.Viewer.ViewModels;

// Bindable wrapper around the active pipeline's running stats. Owns a
// DispatcherTimer that polls the session each second and formats the
// numbers for the sidebar's Processing Stats panel. Lifetime is the same
// as the parent VM; the underlying session is swapped in/out via Start/Stop.
public sealed class PipelineStatsViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    private DispatcherTimer? _timer;
    private IPipelineSession? _session;
    private long _lastTotalAppended;
    private DateTime _lastSampleTime;

    private string _eventRateText = "0 ev/s";
    public string EventRateText
    {
        get => _eventRateText;
        private set => SetProperty(ref _eventRateText, value);
    }

    private string _processingTimeText = "—";
    public string ProcessingTimeText
    {
        get => _processingTimeText;
        private set => SetProperty(ref _processingTimeText, value);
    }

    private string _renderTimeText = "—";
    public string RenderTimeText
    {
        get => _renderTimeText;
        private set => SetProperty(ref _renderTimeText, value);
    }

    public void Start(IPipelineSession session)
    {
        _session = session;
        _lastTotalAppended = session.Buffer.TotalAppended;
        _lastSampleTime = DateTime.UtcNow;

        _timer = new DispatcherTimer { Interval = RefreshInterval };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public void Stop()
    {
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer = null;
        }
        _session = null;

        EventRateText = "0 ev/s";
        ProcessingTimeText = "—";
        RenderTimeText = "—";
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_session is null) return;

        var now = DateTime.UtcNow;
        var elapsedSec = (now - _lastSampleTime).TotalSeconds;
        var currentTotal = _session.Buffer.TotalAppended;
        var rate = elapsedSec > 0 ? (currentTotal - _lastTotalAppended) / elapsedSec : 0;
        _lastTotalAppended = currentTotal;
        _lastSampleTime = now;
        EventRateText = $"{rate:0} ev/s";

        var processingTimes = _session.Viewport.GetProcessingTimes();
        ProcessingTimeText = processingTimes.TryGetValue(typeof(OscilloscopeSettings), out var procMs)
            ? $"{procMs:0.000} ms"
            : "—";

        var renderTimes = _session.Viewport.GetRenderingTimes();
        RenderTimeText = renderTimes.TryGetValue(typeof(OscilloscopeFrame), out var renderMs)
            ? $"{renderMs:0.000} ms"
            : "—";
    }

    public void Dispose() => Stop();
}
