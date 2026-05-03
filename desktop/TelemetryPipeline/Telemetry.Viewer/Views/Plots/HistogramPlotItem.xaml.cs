using System.Windows;
using System.Windows.Media;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.ContextMenu;
using Telemetry.Viewer.Views.Worksheet;

namespace Telemetry.Viewer.Views.Plots
{
    public partial class HistogramPlotItem : PlotItem
    {
        public HistogramPlotItem()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private HistogramSettings Hist => (HistogramSettings)DataContext;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Settings.PropertyChanged += (_, _) => ApplySettings();
            ApplySettings();

            histogramPlot.Plot.RenderManager.RenderFinished += OnRenderFinished;
            histogramPlot.Refresh();
            BroadcastDataArea();
        }

        private void OnRenderFinished(object? sender, ScottPlot.RenderDetails e)
            => histogramPlot.Dispatcher.Invoke(BroadcastDataArea);

        private void BroadcastDataArea()
        {
            var px = histogramPlot.Plot.RenderManager.LastRender.DataRect;
            var dpi = VisualTreeHelper.GetDpi(histogramPlot);
            var rect = new Rect(
                px.Left   / dpi.DpiScaleX,
                px.Top    / dpi.DpiScaleY,
                px.Width  / dpi.DpiScaleX,
                px.Height / dpi.DpiScaleY);
            RaiseDataAreaChanged(rect);
        }

        // X axis runs in bin-position space (0..BinCount); the AxisTicks helper
        // places labels at data-equivalent positions for the chosen scale.
        // Y axis is always linear count, with SI-prefix labels (k, M, G).
        private void ApplySettings()
        {
            histogramPlot.Plot.Clear();

            histogramPlot.Background = Brushes.Transparent;
            histogramPlot.Plot.FigureBackground.Color = ScottPlot.Colors.Transparent;
            histogramPlot.Plot.DataBackground.Color = ScottPlot.Colors.White;
            histogramPlot.Plot.Grid.IsVisible = false;

            histogramPlot.Plot.Title($"{Hist.Param} histogram (ch {Hist.ChannelId})");
            histogramPlot.Plot.XLabel(Hist.Param.ToString());
            histogramPlot.Plot.YLabel("Count");

            histogramPlot.Plot.Axes.Bottom.TickGenerator =
                AxisTicks.ForBinPositionX(Hist.MinRange, Hist.MaxRange, Hist.BinCount, Hist.Scale);
            histogramPlot.Plot.Axes.Left.TickGenerator = AxisTicks.ForLinearY();

            histogramPlot.Plot.Axes.SetLimitsX(0, Hist.BinCount);
            histogramPlot.Refresh();
        }

        // RenderingEngine guarantees this runs on the UI thread. Bars are drawn
        // in bin-position space — bin i centered at (i + 0.5), width 1 — so
        // log/linear scale appears purely via the X axis tick layout.
        public override void Render(ProcessedData data)
        {
            if (data is not HistogramFrame frame)
                return;

            histogramPlot.Plot.Clear();

            long maxCount = 0;
            var bars = new ScottPlot.Bar[frame.Bins.Count];
            for (int i = 0; i < frame.Bins.Count; i++)
            {
                var c = frame.Bins[i].Count;
                if (c > maxCount) maxCount = c;
                bars[i] = new ScottPlot.Bar
                {
                    Position    = i + 0.5,
                    Value       = c,
                    Size        = 1.0,
                    FillColor   = ScottPlot.Colors.SteelBlue,
                    LineWidth = 0,
                    LineColor = ScottPlot.Colors.Transparent,
                };
            }
            histogramPlot.Plot.Add.Bars(bars);

            // X locked to bin-position space; Y always anchored at 0 with a
            // touch of headroom on top so the tallest bar isn't flush against
            // the axis.
            histogramPlot.Plot.Axes.SetLimitsX(0, Hist.BinCount);
            histogramPlot.Plot.Axes.SetLimitsY(0, Math.Max(1, maxCount * 1.05));
            histogramPlot.Refresh();
        }

        public override void AttachContextMenu(Func<IReadOnlyList<ContextMenuProvider>> contextMenuProvider)
            => PlotContextMenuFactory.Attach(histogramPlot, Container!.DragLayer, contextMenuProvider);
    }
}
