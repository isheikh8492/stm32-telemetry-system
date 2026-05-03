using System.Windows;
using System.Windows.Media;
using ScottPlot.DataSources;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.ContextMenu;
using Telemetry.Viewer.Views.Worksheet;

namespace Telemetry.Viewer.Views.Plots
{
    public partial class OscilloscopePlotItem : PlotItem
    {
        private ScottPlot.Plottables.Signal? _eventSignal;

        public OscilloscopePlotItem()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private OscilloscopeSettings Osc => (OscilloscopeSettings)DataContext;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Settings.PropertyChanged += (_, _) => ApplySettings();
            ApplySettings();

            // Subscribe AFTER the control is in the visual tree and has its
            // real size — early-render rects (bitmap not yet sized) are bogus.
            oscilloscopePlot.Plot.RenderManager.RenderFinished += OnRenderFinished;
            oscilloscopePlot.Refresh();
            BroadcastDataArea();
        }

        // RenderFinished can fire on a non-UI thread; marshal to UI dispatcher
        // and read LastRender there so the rect reflects the laid-out size.
        private void OnRenderFinished(object? sender, ScottPlot.RenderDetails e)
        {
            oscilloscopePlot.Dispatcher.Invoke(BroadcastDataArea);
        }

        private void BroadcastDataArea()
        {
            var px = oscilloscopePlot.Plot.RenderManager.LastRender.DataRect;
            var dpi = VisualTreeHelper.GetDpi(oscilloscopePlot);
            var rect = new Rect(
                px.Left   / dpi.DpiScaleX,
                px.Top    / dpi.DpiScaleY,
                px.Width  / dpi.DpiScaleX,
                px.Height / dpi.DpiScaleY);
            RaiseDataAreaChanged(rect);
        }

        // Settings-driven scaffolding: axes, labels, ranges, title. Idempotent —
        // called on Loaded and on every Settings.PropertyChanged. Plot.Clear
        // wipes plottables so the next Render call recreates the data series.
        private void ApplySettings()
        {
            oscilloscopePlot.Plot.Clear();
            _eventSignal = null;

            // Outside the data rect (title / axis labels area) is transparent
            // so the worksheet grid shows through; only the data area itself
            // renders on a white surface.
            oscilloscopePlot.Background = Brushes.Transparent;
            oscilloscopePlot.Plot.FigureBackground.Color = ScottPlot.Colors.Transparent;
            oscilloscopePlot.Plot.DataBackground.Color = ScottPlot.Colors.White;

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
        public override void Render(ProcessedData data)
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

        public override void AttachContextMenu(Func<IReadOnlyList<ContextMenuProvider>> contextMenuProvider)
            => PlotContextMenuFactory.Attach(oscilloscopePlot, contextMenuProvider);
    }
}
