using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScottPlot.DataSources;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Models.Worksheet;
using Telemetry.Viewer.Services.ContextMenu;

namespace Telemetry.Viewer.Views.Plots
{
    public partial class OscilloscopePlotView : UserControl, IPlotView, IPlotDataAreaProvider
    {
        private ScottPlot.Plottables.Signal? _eventSignal;

        public event Action<Rect>? DataAreaChanged;

        public OscilloscopePlotView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        // RenderFinished can fire on a non-UI thread and fires multiple times
        // (including before the plot has its real size). Subscribe only after
        // the visual tree is up, marshal to the UI dispatcher, and read
        // LastRender there so the rect reflects the laid-out size.
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
            DataAreaChanged?.Invoke(rect);
        }

        public Guid Id => Settings.PlotId;

        public new string Name => Settings.DisplayName;

        public PlotSettings Settings => (PlotSettings)DataContext;

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

        // Settings-driven scaffolding: axes, labels, ranges, title. Idempotent —
        // called on Loaded and on every Settings.PropertyChanged. Plot.Clear
        // wipes plottables so the next Render call recreates the data series.
        private void ApplySettings()
        {
            oscilloscopePlot.Plot.Clear();
            _eventSignal = null;

            // Outside the data rect (title / axis labels area) is transparent
            // so the worksheet grid shows through; only the data area itself
            // renders on a white surface. Thumbs hug the data rect, so users
            // align plots to the grid by the data — not by the chrome.
            oscilloscopePlot.Background = System.Windows.Media.Brushes.Transparent;
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
