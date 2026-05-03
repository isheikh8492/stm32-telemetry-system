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
    public partial class OscilloscopePlotView : UserControl, IPlotView
    {
        private ScottPlot.Plottables.Signal? _eventSignal;

        public OscilloscopePlotView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        public Guid Id => Settings.PlotId;

        public new string Name => Settings.DisplayName;

        public PlotSettings Settings => (PlotSettings)DataContext;

        private OscilloscopeSettings Osc => (OscilloscopeSettings)DataContext;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Settings.PropertyChanged += (_, _) => ApplySettings();
            ApplySettings();

            var window = Window.GetWindow(this);
            if (window?.DataContext is MainWindowViewModel vm)
                vm.Worksheet.NotifyViewLoaded(this);
        }

        // Settings-driven scaffolding: axes, labels, ranges, title. Idempotent —
        // called on Loaded and on every Settings.PropertyChanged. Plot.Clear
        // wipes plottables so the next Render call recreates the data series.
        private void ApplySettings()
        {
            oscilloscopePlot.Plot.Clear();
            _eventSignal = null;

            oscilloscopePlot.Plot.Axes.Rules.Clear();
            oscilloscopePlot.Plot.Axes.Rules.Add(new ScottPlot.AxisRules.LockedVertical(oscilloscopePlot.Plot.Axes.Left, 0, 5000));
            oscilloscopePlot.Plot.Axes.Rules.Add(new ScottPlot.AxisRules.LockedHorizontal(oscilloscopePlot.Plot.Axes.Bottom, 0, 32));

            oscilloscopePlot.Plot.Title($"Live Telemetry — ch {Osc.ChannelId}");
            oscilloscopePlot.Plot.XLabel("Sample");
            oscilloscopePlot.Plot.YLabel("ADC");
            oscilloscopePlot.Plot.Axes.SetLimits(left: 0, right: 32, bottom: 0, top: 5000);
            oscilloscopePlot.Refresh();
        }

        // RenderingEngine guarantees this runs on the UI thread.
        public void Render(ProcessedData data)
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
            oscilloscopePlot.Refresh();
        }

        public void AttachContextMenu(Func<IReadOnlyList<ContextMenuProvider>> contextMenuProvider)
            => PlotContextMenuFactory.Attach(oscilloscopePlot, contextMenuProvider);
    }
}
