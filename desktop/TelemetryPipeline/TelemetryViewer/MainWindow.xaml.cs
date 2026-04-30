using System.Threading.Tasks;
using System.Windows;
using ScottPlot;
using ScottPlot.DataSources;
using Telemetry.Core.Models;
using Telemetry.IO;

namespace TelemetryViewer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private SerialReader? _serialReader;
        private Task? _readerTask;
        private ScottPlot.Plottables.Signal? _eventSignal;

        public MainWindow()
        {
            InitializeComponent();

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            BaudRateComboBox.ItemsSource = new[] { SerialReader.DefaultBaudRate };
            BaudRateComboBox.SelectedItem = SerialReader.DefaultBaudRate;
            BaudRateComboBox.IsEnabled = false;

            LoadAvailablePorts();

            try
            {
                InitializeWorksheetPlot();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Plot initialization failed: {ex.Message}", "Telemetry Viewer Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InitializeWorksheetPlot()
        {
            double[] initialSamples = [0d];

            oscilloscopePlotView.Plot.Clear();
            oscilloscopePlotView.Plot.Axes.Rules.Clear();
            oscilloscopePlotView.Plot.Axes.Rules.Add(new ScottPlot.AxisRules.LockedVertical(oscilloscopePlotView.Plot.Axes.Left, 0, 5000));
            _eventSignal = oscilloscopePlotView.Plot.Add.SignalConst(initialSamples);
            _eventSignal.MaximumMarkerSize = 0;
            oscilloscopePlotView.Plot.Title("Live Telemetry");
            oscilloscopePlotView.Plot.XLabel("Sample");
            oscilloscopePlotView.Plot.YLabel("ADC");
            oscilloscopePlotView.Plot.Axes.SetLimits(left: 0, right: 32, bottom: 0, top: 5000);
            oscilloscopePlotView.Refresh();
        }

        private void UpdateEventPlot(Event telemetryEvent)
        {
            var sampleValues = telemetryEvent.Samples.Select(static sample => (double)sample).ToArray();

            Dispatcher.Invoke(() =>
            {
                if (_eventSignal is null)
                {
                    _eventSignal = oscilloscopePlotView.Plot.Add.SignalConst(sampleValues);
                }
                else
                {
                    _eventSignal.Data = new SignalConstSource<double>(sampleValues, 1);
                }

                oscilloscopePlotView.Plot.Axes.SetLimits(left: 0, right: Math.Max(1, sampleValues.Length - 1), bottom: 0, top: 5000);
                oscilloscopePlotView.Refresh();
            });
        }

        private void LoadAvailablePorts()
        {
            var ports = SerialReader.GetAvailablePorts();

            PortComboBox.ItemsSource = ports;

            if (ports.Length > 0)
            {
                PortComboBox.SelectedIndex = 0;
            }
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_serialReader is not null)
            {
                DisconnectSerialReader();
                return;
            }

            if (PortComboBox.SelectedItem is not string portName || string.IsNullOrWhiteSpace(portName))
            {
                MessageBox.Show(this, "Select a COM port.", "Connection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var baudRate = SerialReader.DefaultBaudRate;

            try
            {
                _serialReader = new SerialReader(portName, baudRate);
                _serialReader.EventReceived += SerialReader_EventReceived;
                _serialReader.ErrorOccurred += SerialReader_ErrorOccurred;
                _readerTask = Task.Run(() => _serialReader.Start());

                ConnectButton.Content = "Disconnect";
                PortComboBox.IsEnabled = false;
                BaudRateComboBox.IsEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                DisconnectSerialReader();
            }
        }

        private void SerialReader_EventReceived(Event telemetryEvent)
        {
            UpdateEventPlot(telemetryEvent);
        }

        private void SerialReader_ErrorOccurred(string message)
        {
            Dispatcher.Invoke(() =>
                MessageBox.Show(this, message, "Serial Reader Error", MessageBoxButton.OK, MessageBoxImage.Error));
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            DisconnectSerialReader();
        }

        private void DisconnectSerialReader()
        {
            if (_serialReader is not null)
            {
                _serialReader.EventReceived -= SerialReader_EventReceived;
                _serialReader.ErrorOccurred -= SerialReader_ErrorOccurred;
                _serialReader.Stop();
                _serialReader.Dispose();
                _serialReader = null;
            }

            _readerTask = null;
            ConnectButton.Content = "Connect";
            PortComboBox.IsEnabled = true;
            BaudRateComboBox.IsEnabled = false;
        }
    }
}