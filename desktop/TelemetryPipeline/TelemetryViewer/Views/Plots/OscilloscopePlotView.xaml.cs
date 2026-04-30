using System.Windows;
using System.Windows.Controls;
using ScottPlot.DataSources;
using Telemetry.Core.Models;

namespace TelemetryViewer.Views.Plots
{
    public partial class OscilloscopePlotView : UserControl
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

        public void UpdatePlot(Event telemetryEvent)
        {
            var sampleValues = telemetryEvent.Samples.Select(static sample => (double)sample).ToArray();

            Dispatcher.Invoke(() =>
            {
                if (_eventSignal is null)
                {
                    _eventSignal = oscilloscopePlot.Plot.Add.SignalConst(sampleValues);
                    _eventSignal.MaximumMarkerSize = 0;
                }
                else
                {
                    _eventSignal.Data = new SignalConstSource<double>(sampleValues, 1);
                }

                oscilloscopePlot.Plot.Axes.SetLimits(left: 0, right: Math.Max(1, sampleValues.Length - 1), bottom: 0, top: 5000);
                oscilloscopePlot.Refresh();
            });
        }
    }
}