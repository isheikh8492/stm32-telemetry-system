using System.Windows;
using System.Windows.Controls;
using ScottPlot.DataSources;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Models.Worksheet;
using Telemetry.Viewer.Services.ContextMenu;
using Telemetry.Viewer.ViewModels;

namespace Telemetry.Viewer.Views.Plots
{
    // The View IS the IPlotView — Settings comes from the DataContext bound by
    // the ItemsControl DataTemplate (each item is a PlotSettings record).
    public partial class OscilloscopePlotView : UserControl, IPlotView, IContextMenuTarget
    {
        private ScottPlot.Plottables.Signal? _eventSignal;
        private bool _initialized;

        public OscilloscopePlotView()
        {
            InitializeComponent();
            Loaded += OscilloscopePlotView_Loaded;
        }

        public Guid Id => Settings.PlotId;

        // 'new' because FrameworkElement.Name (the XAML x:Name) is unrelated
        // to the IPlotView display name we want here.
        public new string Name => Settings switch
        {
            OscilloscopeSettings o => $"Oscilloscope (ch {o.ChannelId})",
            HistogramSettings    h => $"Histogram (ch {h.ChannelId})",
            _                      => "Plot"
        };

        public PlotSettings Settings => (PlotSettings)DataContext;

        private void OscilloscopePlotView_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is not PlotSettings)
                return;

            try
            {
                var window = Window.GetWindow(this);
                if (window?.DataContext is MainWindowViewModel vm)
                    vm.NotifyPlotViewLoaded(this);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Plot initialization failed: {ex.Message}", "Telemetry Viewer Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void InitializeStaticLayer()
        {
            if (_initialized)
                return;

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

            _initialized = true;
        }

        public void RenderDynamicLayer(ProcessedData data)
        {
            if (data is not OscilloscopeFrame frame)
                return;

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
            oscilloscopePlot.Menu = null;

            var menu = new System.Windows.Controls.ContextMenu();
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
