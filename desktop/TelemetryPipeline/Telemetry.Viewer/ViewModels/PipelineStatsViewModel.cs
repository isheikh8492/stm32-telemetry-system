using System.Collections.ObjectModel;
using System.Windows.Threading;
using Telemetry.Viewer.Common;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.Pipeline;

namespace Telemetry.Viewer.ViewModels;

// Bindable wrapper around the active pipeline's running stats. Owns a
// DispatcherTimer that polls the session each second and formats the
// numbers for the sidebar's Processing Stats panel.
//
// Per-plot-type timing: PlotStats has one row per PlotType (Oscilloscope,
// Histogram, Pseudocolor, SpectralRibbon) with the average processing +
// render time across all instances of that type. Rows are created once on
// Start and updated in place each tick — no list churn.
public sealed class PipelineStatsViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    private DispatcherTimer? _timer;
    private IPipelineSession? _session;
    private long _lastTotalAppended;
    private DateTime _lastSampleTime;

    public ObservableCollection<PlotStatsRow> PlotStats { get; }

    public PipelineStatsViewModel()
    {
        // Two rows per plot type — Processing first, then Rendering — so the
        // sidebar reads as a flat list grouped by type.
        var rows = new List<PlotStatsRow>();
        foreach (var t in Enum.GetValues<PlotType>())
        {
            rows.Add(new PlotStatsRow(t, isProcessing: true));
            rows.Add(new PlotStatsRow(t, isProcessing: false));
        }
        PlotStats = new ObservableCollection<PlotStatsRow>(rows);
    }

    private string _totalEventsText = "0";
    public string TotalEventsText
    {
        get => _totalEventsText;
        private set => SetProperty(ref _totalEventsText, value);
    }

    private string _eventRateText = "0 ev/s";
    public string EventRateText
    {
        get => _eventRateText;
        private set => SetProperty(ref _eventRateText, value);
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

        TotalEventsText = "0";
        EventRateText = "0 ev/s";
        foreach (var row in PlotStats)
            row.Value = "—";
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
        TotalEventsText = $"{currentTotal:N0}";
        EventRateText = $"{rate:0} ev/s";

        var processing = _session.Viewport.GetProcessingTimes();
        var rendering  = _session.Viewport.GetRenderingTimes();

        foreach (var row in PlotStats)
        {
            var dict = row.IsProcessing ? processing : rendering;
            row.Value = dict.TryGetValue(row.Type, out var ms) ? $"{ms:0.000} ms" : "—";
        }
    }

    public void Dispose() => Stop();
}
