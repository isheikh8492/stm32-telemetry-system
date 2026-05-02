using System.Windows;
using System.Windows.Controls;
using ScottPlot.DataSources;
using Telemetry.Engine;

namespace TelemetryViewer.Views.Plots
{
    public partial class OscilloscopePlotView : UserControl, IRenderTarget, IContextMenuTarget
    {
        private ScottPlot.Plottables.Signal? _eventSignal;

        public OscilloscopePlotView()
        {
            InitializeComponent();
            Loaded += OscilloscopePlotView_Loaded;
        }

        private void OscilloscopePlotView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                InitializePlot();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Plot initialization failed: {ex.Message}", "Telemetry Viewer Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InitializePlot()
        {
            double[] initialSamples = [0d];

            oscilloscopePlot.Plot.Clear();
            oscilloscopePlot.Plot.Axes.Rules.Clear();
            oscilloscopePlot.Plot.Axes.Rules.Add(new ScottPlot.AxisRules.LockedVertical(oscilloscopePlot.Plot.Axes.Left, 0, 5000));
            _eventSignal = oscilloscopePlot.Plot.Add.SignalConst(initialSamples);
            _eventSignal.MaximumMarkerSize = 0;
            oscilloscopePlot.Plot.Title("Live Telemetry");
            oscilloscopePlot.Plot.XLabel("Sample");
            oscilloscopePlot.Plot.YLabel("ADC");
            oscilloscopePlot.Plot.Axes.SetLimits(left: 0, right: 32, bottom: 0, top: 5000);
            oscilloscopePlot.Refresh();
        }

        // RenderingEngine guarantees this is invoked on the UI thread.
        public void Render(ProcessedData data)
        {
            if (data is not OscilloscopeFrame frame)
                return;

            // Manual ushort -> double copy avoids LINQ iterator allocation in the hot path.
            var samples = frame.Samples;
            var values = new double[samples.Count];
            for (int i = 0; i < samples.Count; i++)
                values[i] = samples[i];

            if (_eventSignal is null)
            {
                _eventSignal = oscilloscopePlot.Plot.Add.SignalConst(values);
                _eventSignal.MaximumMarkerSize = 0;
            }
            else
            {
                _eventSignal.Data = new SignalConstSource<double>(values, 1);
            }

            oscilloscopePlot.Plot.Axes.SetLimits(left: 0, right: Math.Max(1, values.Length - 1), bottom: 0, top: 5000);
            oscilloscopePlot.Refresh();
        }

        public void AttachContextMenu(Func<IReadOnlyList<ContextMenuEntry>> entryFactory)
        {
            // Disable ScottPlot's default right-click menu so ours wins.
            oscilloscopePlot.Menu = null;

            var menu = new ContextMenu();
            menu.Opened += (_, _) =>
            {
                menu.Items.Clear();
                foreach (var entry in entryFactory())
                {
                    var item = new MenuItem { Header = entry.Label };
                    var capturedEntry = entry;
                    item.Click += (_, _) => capturedEntry.OnInvoke();
                    menu.Items.Add(item);
                }
            };
            oscilloscopePlot.ContextMenu = menu;
        }
    }
}
