using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Telemetry.Engine;
using Telemetry.IO;

namespace TelemetryViewer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const int BufferCapacity = 10_000;
        private static readonly TimeSpan StatsRefreshInterval = TimeSpan.FromSeconds(1);
        private static readonly int[] SupportedBaudRates = { 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600 };

        private SerialReader? _serialReader;
        private SerialProducer? _producer;
        private RingBuffer? _buffer;
        private BufferConsumer? _consumer;
        private CancellationTokenSource? _consumerCts;
        private Task? _consumerTask;
        private ViewportSession? _viewport;

        private DispatcherTimer? _statsTimer;
        private long _lastTotalAppended;
        private DateTime _lastStatsSampleTime;

        public MainWindow()
        {
            InitializeComponent();

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            BaudRateComboBox.ItemsSource = SupportedBaudRates;
            BaudRateComboBox.SelectedItem = SerialReader.DefaultBaudRate;
            BaudRateComboBox.IsEnabled = true;

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
            if (_producer is not null)
            {
                Disconnect();
                return;
            }

            if (PortComboBox.SelectedItem is not string portName || string.IsNullOrWhiteSpace(portName))
            {
                MessageBox.Show(this, "Select a COM port.", "Connection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (BaudRateComboBox.SelectedItem is not int baudRate)
            {
                MessageBox.Show(this, "Select a baud rate.", "Connection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                StartPipeline(portName, baudRate);

                ConnectButton.Content = "Disconnect";
                PortComboBox.IsEnabled = false;
                BaudRateComboBox.IsEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Disconnect();
            }
        }

        private void StartPipeline(string portName, int baudRate)
        {
            // 1. Reader & error subscription
            _serialReader = new SerialReader(portName, baudRate);
            _serialReader.ErrorOccurred += SerialReader_ErrorOccurred;

            // 2. Buffer + producer/consumer pipeline
            _buffer = new RingBuffer(BufferCapacity);
            _producer = new SerialProducer(_serialReader);
            _consumer = new BufferConsumer(_producer.Reader, _buffer);

            // 3. Viewport session — owns processing + rendering engines
            var uiContext = SynchronizationContext.Current
                ?? throw new InvalidOperationException("UI SynchronizationContext is null.");
            _viewport = new ViewportSession(_buffer, uiContext);

            // 4. Register the oscilloscope plot
            var oscilloscopeId = Guid.NewGuid();
            _viewport.Register(new OscilloscopeSettings(oscilloscopeId), oscilloscopePlotView);

            // 5. Start everything: consumer task first (so it's awaiting), then producer (which feeds), then engines
            _consumerCts = new CancellationTokenSource();
            _consumerTask = Task.Run(() => _consumer.RunAsync(_consumerCts.Token));
            _producer.Start();
            _viewport.Start();

            // 6. Stats timer (UI thread, 1 Hz)
            _lastTotalAppended = _buffer.TotalAppended;
            _lastStatsSampleTime = DateTime.UtcNow;
            _statsTimer = new DispatcherTimer { Interval = StatsRefreshInterval };
            _statsTimer.Tick += StatsTimer_Tick;
            _statsTimer.Start();
        }

        private void StatsTimer_Tick(object? sender, EventArgs e)
        {
            if (_buffer is null || _viewport is null)
                return;

            // Event rate from RingBuffer.TotalAppended delta over wall-clock seconds.
            var now = DateTime.UtcNow;
            var elapsedSec = (now - _lastStatsSampleTime).TotalSeconds;
            var currentTotal = _buffer.TotalAppended;
            var rate = elapsedSec > 0 ? (currentTotal - _lastTotalAppended) / elapsedSec : 0;
            _lastTotalAppended = currentTotal;
            _lastStatsSampleTime = now;
            EventRateText.Text = $"{rate:0} ev/s";

            var processingTimes = _viewport.GetProcessingTimes();
            ProcessingTimeText.Text = processingTimes.TryGetValue(typeof(OscilloscopeSettings), out var procMs)
                ? $"{procMs:0.000} ms"
                : "—";

            var renderTimes = _viewport.GetRenderingTimes();
            RenderTimeText.Text = renderTimes.TryGetValue(typeof(OscilloscopeFrame), out var renderMs)
                ? $"{renderMs:0.000} ms"
                : "—";
        }

        private void SerialReader_ErrorOccurred(string message)
        {
            Dispatcher.Invoke(() =>
                MessageBox.Show(this, message, "Serial Reader Error", MessageBoxButton.OK, MessageBoxImage.Error));
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            Disconnect();
        }

        private void Disconnect()
        {
            if (_statsTimer is not null)
            {
                _statsTimer.Stop();
                _statsTimer.Tick -= StatsTimer_Tick;
                _statsTimer = null;
            }

            _viewport?.Stop();
            _viewport?.Dispose();
            _viewport = null;

            _consumerCts?.Cancel();
            _producer?.Dispose();
            _producer = null;
            _consumerTask = null;

            if (_serialReader is not null)
            {
                _serialReader.ErrorOccurred -= SerialReader_ErrorOccurred;
                _serialReader.Dispose();
                _serialReader = null;
            }

            _consumerCts?.Dispose();
            _consumerCts = null;

            _buffer = null;
            _consumer = null;

            EventRateText.Text = "0 ev/s";
            ProcessingTimeText.Text = "—";
            RenderTimeText.Text = "—";

            ConnectButton.Content = "Connect";
            PortComboBox.IsEnabled = true;
            BaudRateComboBox.IsEnabled = true;
        }
    }
}
