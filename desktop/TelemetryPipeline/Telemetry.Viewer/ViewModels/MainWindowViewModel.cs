using System.Threading;
using System.Windows;
using Telemetry.IO;
using Telemetry.Viewer.Common;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Models.Worksheet;
using Telemetry.Viewer.Services.Pipeline;
using Telemetry.Viewer.Views.Worksheet;

namespace Telemetry.Viewer.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private static readonly int[] DefaultBaudRates =
        { 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600, 2000000 };

    private readonly IPipelineFactory _pipelineFactory;

    private IPipelineSession? _session;

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

    // ---- Pipeline stats (own VM; bound by sidebar's stats panel) ----

    public PipelineStatsViewModel Stats { get; } = new();

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
            Stats.Start(_session);
            IsConnected = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Disconnect();
        }
    }

    private void Disconnect()
    {
        Stats.Stop();
        Worksheet.UnbindSession();

        if (_session is not null)
        {
            _session.Reader.ErrorOccurred -= OnSerialError;
            _session.Dispose();
            _session = null;
        }

        IsConnected = false;
    }

    private void AddOscilloscope()
    {
        var settings = new OscilloscopeSettings(plotId: Guid.NewGuid(), channelId: 0);
        Worksheet.AddPlot(settings);
    }

    private void OnSerialError(string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
            MessageBox.Show(message, "Serial Reader Error", MessageBoxButton.OK, MessageBoxImage.Error));
    }

    public void Dispose() => Disconnect();
}
