using System.Threading.Tasks;
using System.Windows;
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
            oscilloscopePlotView.UpdatePlot(telemetryEvent);
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