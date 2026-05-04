using System.Windows.Input;
using Telemetry.IO;
using Telemetry.Viewer.Common;
using Telemetry.Viewer.Services.Dialogs;
using Telemetry.Viewer.Services.Pipeline;
using Telemetry.Viewer.Views.Plots;
using Telemetry.Viewer.Views.Worksheet;

namespace Telemetry.Viewer.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IPipelineFactory _pipelineFactory;
    private readonly IPortDiscovery _ports;
    private readonly IDialogService _dialogs;

    private readonly RelayCommand _toggleConnectionCommand;

    private IPipelineSession? _session;

    public MainWindowViewModel(
        IPipelineFactory pipelineFactory,
        IPortDiscovery ports,
        IDialogService dialogs)
    {
        _pipelineFactory = pipelineFactory;
        _ports = ports;
        _dialogs = dialogs;

        SupportedBaudRates = ports.SupportedBaudRates;
        _selectedBaudRate = ports.DefaultBaudRate;

        _toggleConnectionCommand = new RelayCommand(ToggleConnection, CanToggleConnection);
        ToggleConnectionCommand = _toggleConnectionCommand;
        RefreshPortsCommand = new RelayCommand(LoadAvailablePorts);

        // Plot-type registry. Each per-plot wiring file (toolbar label,
        // default size, factories, menu) lives next to its view; this is the
        // single startup point that pulls them in. Adding a new plot type:
        // create <Type>Plot.cs, add one Register call here.
        OscilloscopePlot.Register(Worksheet, _dialogs);
        HistogramPlot.Register(Worksheet, _dialogs);
    }

    // Called once by MainWindow on Loaded. Keeps the ctor side-effect-free
    // (no OS calls during construction → easier to test, faster to instantiate).
    public void Initialize() => LoadAvailablePorts();

    // ---- Child VMs (app-lifetime) ----

    public Worksheet Worksheet { get; } = new();
    public PipelineStatsViewModel Stats { get; } = new();

    // ---- Bindable connection state ----

    public IReadOnlyList<int> SupportedBaudRates { get; }

    private IReadOnlyList<string> _availablePorts = Array.Empty<string>();
    public IReadOnlyList<string> AvailablePorts
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
                _toggleConnectionCommand.RaiseCanExecuteChanged();
        }
    }

    private int _selectedBaudRate;
    public int SelectedBaudRate
    {
        get => _selectedBaudRate;
        set
        {
            if (SetProperty(ref _selectedBaudRate, value))
                _toggleConnectionCommand.RaiseCanExecuteChanged();
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
                _toggleConnectionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsDisconnected => !IsConnected;

    // ---- Commands (public surface = ICommand; concrete kept private for RaiseCanExecuteChanged) ----

    public ICommand ToggleConnectionCommand { get; }
    public ICommand RefreshPortsCommand { get; }

    // ---- Internals ----

    private bool CanToggleConnection()
    {
        if (IsConnected) return true;
        return !string.IsNullOrWhiteSpace(SelectedPort) && SelectedBaudRate > 0;
    }

    private void LoadAvailablePorts()
    {
        var ports = _ports.GetAvailablePorts();
        AvailablePorts = ports;
        if (ports.Count > 0 && string.IsNullOrEmpty(SelectedPort))
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
            _session = _pipelineFactory.Create(SelectedPort!, SelectedBaudRate);
            _session.ErrorOccurred += OnSessionError;

            Worksheet.BindSession(_session.Viewport);
            _session.Start();
            Stats.Start(_session);
            IsConnected = true;
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(ex.Message, "Connection Error");
            Disconnect();
        }
    }

    private void Disconnect()
    {
        Stats.Stop();
        Worksheet.UnbindSession();

        if (_session is not null)
        {
            _session.ErrorOccurred -= OnSessionError;
            _session.Dispose();
            _session = null;
        }

        IsConnected = false;
    }

    // PipelineSession marshals serial reader errors onto the UI thread, so
    // we just show the dialog — no Dispatcher.Invoke needed.
    private void OnSessionError(string message)
        => _dialogs.ShowError(message, "Serial Reader Error");

    public void Dispose()
    {
        Disconnect();
        Stats.Dispose();
    }
}
