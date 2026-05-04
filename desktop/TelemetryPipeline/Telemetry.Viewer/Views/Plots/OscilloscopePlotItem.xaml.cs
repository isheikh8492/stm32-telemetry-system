using System.Windows;
using System.Windows.Media;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.ContextMenu;
using Telemetry.Viewer.Views.Plots.Axes;
using Telemetry.Viewer.Views.Worksheet;

namespace Telemetry.Viewer.Views.Plots
{
    public partial class OscilloscopePlotItem : PlotItem
    {
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
            => oscilloscopePlot.Dispatcher.Invoke(BroadcastDataArea);

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

        // Settings-driven scaffolding: axes, labels, ranges. Idempotent —
        // called on Loaded and on every Settings.PropertyChanged. The data
        // bitmap is painted by OscilloscopePlotProcessor (off the UI thread).
        private void ApplySettings()
        {
            oscilloscopePlot.Plot.Clear();

            // Outside the data rect (axis labels area) is transparent so the
            // worksheet grid shows through; only the data area itself renders
            // on a white surface, which the painted bitmap then overlays.
            oscilloscopePlot.Background = Brushes.Transparent;
            oscilloscopePlot.Plot.FigureBackground.Color = ScottPlot.Colors.Transparent;
            oscilloscopePlot.Plot.DataBackground.Color = ScottPlot.Colors.White;

            oscilloscopePlot.Plot.Axes.Rules.Clear();
            oscilloscopePlot.Plot.Axes.Rules.Add(new ScottPlot.AxisRules.LockedVertical(oscilloscopePlot.Plot.Axes.Left, 0, 5000));
            oscilloscopePlot.Plot.Axes.Rules.Add(new ScottPlot.AxisRules.LockedHorizontal(oscilloscopePlot.Plot.Axes.Bottom, 0, 32));

            oscilloscopePlot.Plot.Grid.IsVisible = false;

            oscilloscopePlot.Plot.Axes.Left.TickGenerator   = OscilloscopeTickGenerator.BuildY();
            oscilloscopePlot.Plot.Axes.Bottom.TickGenerator = OscilloscopeTickGenerator.BuildX();

            oscilloscopePlot.Plot.XLabel("Window (s)");
            oscilloscopePlot.Plot.YLabel("ADC");
            oscilloscopePlot.Plot.Axes.SetLimits(left: 0, right: 32, bottom: 0, top: 5000);
            oscilloscopePlot.Refresh();
        }

        public override void AttachContextMenu(Func<IReadOnlyList<ContextMenuProvider>> contextMenuProvider)
            => PlotContextMenuFactory.Attach(oscilloscopePlot, Container!.DragLayer, contextMenuProvider);
    }
}
