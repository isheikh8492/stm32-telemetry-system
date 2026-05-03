using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Telemetry.IO;
using Telemetry.Viewer.Common;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Models.Worksheet;
using Telemetry.Viewer.Services.Pipeline;
using Telemetry.Viewer.Views.Worksheet;

namespace Telemetry.Viewer.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan StatsRefreshInterval = TimeSpan.FromSeconds(1);
    private static readonly int[] DefaultBaudRates =
        { 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600, 2000000 };

    private readonly IPipelineFactory _pipelineFactory;

    private IPipelineSession? _session;
    private DispatcherTimer? _statsTimer;
    private long _lastTotalAppended;
    private DateTime _lastStatsSampleTime;

    public MainWindowViewModel(IPipelineFactory pipelineFactory)
    {
        _pipelineFactory = pipelineFactory;

        SupportedBaudRates = DefaultBaudRates;
        _selectedBaudRate = SerialReader.DefaultBaudRate;

        ToggleConnectionCommand = new RelayCommand(ToggleConnection, CanToggleConnection);
        RefreshPortsCommand = new RelayCommand(LoadAvailablePorts);
        AddOscilloscopeCommand = new RelayCommand(AddOscilloscope);

        LoadAvailablePorts();
    }

    // ---- Worksheet (app-lifetime; survives connect/disconnect) ----

    public Worksheet Worksheet { get; } = new();

    // ---- Bindable connection state ----

    public IReadOnlyList<int> SupportedBaudRates { get; }

    private string[] _availablePorts = Array.Empty<string>();
    public string[] AvailablePorts
    {
        get => _availablePorts;
        private set => SetProperty(ref _availablePorts, value);
    }

    private string? _selectedPort;
    public string? SelectedPort
    {
        get => _selectedPort;
        set
        {
            if (SetProperty(ref _selectedPort, value))
                ToggleConnectionCommand.RaiseCanExecuteChanged();
        }
    }

    private int _selectedBaudRate;
    public int SelectedBaudRate
    {
        get => _selectedBaudRate;
        set
        {
            if (SetProperty(ref _selectedBaudRate, value))
                ToggleConnectionCommand.RaiseCanExecuteChanged();
        }
    }

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(IsDisconnected));
                OnPropertyChanged(nameof(ConnectButtonText));
                ToggleConnectionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsDisconnected => !IsConnected;
    public string ConnectButtonText => IsConnected ? "Disconnect" : "Connect";

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

    // ---- Commands ----

    public RelayCommand ToggleConnectionCommand { get; }
    public RelayCommand RefreshPortsCommand { get; }
    public RelayCommand AddOscilloscopeCommand { get; }

    // ---- View handshake (forwarded to Worksheet) ----

    public void NotifyPlotViewLoaded(IPlotView view) => Worksheet.NotifyViewLoaded(view);

    // ---- Internals ----

    private bool CanToggleConnection()
    {
        if (IsConnected) return true;
        return !string.IsNullOrWhiteSpace(SelectedPort) && SelectedBaudRate > 0;
    }

    private void LoadAvailablePorts()
    {
        var ports = SerialReader.GetAvailablePorts();
        AvailablePorts = ports;
        if (ports.Length > 0 && string.IsNullOrEmpty(SelectedPort))
            SelectedPort = ports[0];
    }

    private void ToggleConnection()
    {
        if (IsConnected) Disconnect();
        else             Connect();
    }

    private void Connect()
    {
        if (string.IsNullOrWhiteSpace(SelectedPort))
            return;

        try
        {
            var ui = SynchronizationContext.Current
                ?? throw new InvalidOperationException("UI SynchronizationContext is null.");

            _session = _pipelineFactory.Create(SelectedPort!, SelectedBaudRate, ui);
            _session.Reader.ErrorOccurred += OnSerialError;

            Worksheet.BindSession(_session.Viewport);
            _session.Start();
            IsConnected = true;

            _lastTotalAppended = _session.Buffer.TotalAppended;
            _lastStatsSampleTime = DateTime.UtcNow;
            _statsTimer = new DispatcherTimer { Interval = StatsRefreshInterval };
            _statsTimer.Tick += StatsTimer_Tick;
            _statsTimer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Disconnect();
        }
    }

    private void Disconnect()
    {
        if (_statsTimer is not null)
        {
            _statsTimer.Stop();
            _statsTimer.Tick -= StatsTimer_Tick;
            _statsTimer = null;
        }

        Worksheet.UnbindSession();

        if (_session is not null)
        {
            _session.Reader.ErrorOccurred -= OnSerialError;
            _session.Dispose();
            _session = null;
        }

        IsConnected = false;
        EventRateText = "0 ev/s";
        ProcessingTimeText = "—";
        RenderTimeText = "—";
    }

    private void AddOscilloscope()
    {
        var settings = new OscilloscopeSettings(plotId: Guid.NewGuid(), channelId: 0);
        Worksheet.AddPlot(settings);
    }

    private void StatsTimer_Tick(object? sender, EventArgs e)
    {
        if (_session is null) return;

        var now = DateTime.UtcNow;
        var elapsedSec = (now - _lastStatsSampleTime).TotalSeconds;
        var currentTotal = _session.Buffer.TotalAppended;
        var rate = elapsedSec > 0 ? (currentTotal - _lastTotalAppended) / elapsedSec : 0;
        _lastTotalAppended = currentTotal;
        _lastStatsSampleTime = now;
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

    private void OnSerialError(string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
            MessageBox.Show(message, "Serial Reader Error", MessageBoxButton.OK, MessageBoxImage.Error));
    }

    public void Dispose() => Disconnect();
}
