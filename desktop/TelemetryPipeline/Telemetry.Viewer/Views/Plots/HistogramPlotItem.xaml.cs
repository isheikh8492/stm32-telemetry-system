using ScottPlot.WPF;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Views.Plots.Axes;
using Telemetry.Viewer.Views.Worksheet;

namespace Telemetry.Viewer.Views.Plots
{
    public partial class HistogramPlotItem : PlotItem
    {
        // Tracked so we only Refresh ScottPlot's static layer when YMax
        // actually changes (axis labels follow). At high event rate most
        // frames share the same NiceMax → no Refresh, just a bitmap blit.
        private double _lastYMax;

        public HistogramPlotItem() => InitializeComponent();

        protected override WpfPlot Plot => histogramPlot;

        private HistogramSettings Hist => (HistogramSettings)DataContext;

        // X axis runs in bin-position space (0..BinCount); the AxisItem helper
        // places labels at data-equivalent positions for the chosen scale.
        // Y axis is always linear count, with SI-prefix labels (K, M, B, T).
        protected override void OnApplySettings()
        {
            histogramPlot.Plot.XLabel(Hist.Selection.Label);
            histogramPlot.Plot.YLabel("Count");

            var binCount = (int)Hist.BinCount;
            histogramPlot.Plot.Axes.Bottom.TickGenerator =
                AxisFactory.For(Hist.Scale).BuildBinTickGenerator(Hist.MinRange, Hist.MaxRange, binCount);
            histogramPlot.Plot.Axes.Left.TickGenerator =
                HistogramYAxisItem.CreateTickGenerator();

            histogramPlot.Plot.Axes.SetLimitsX(0, binCount);
            // Empty histogram → NiceMax(0) floor (1K) so axis isn't [-10,10].
            _lastYMax = HistogramYAxisItem.NiceMax(0);
            histogramPlot.Plot.Axes.SetLimitsY(0, _lastYMax);
        }

        protected override void OnRender(ProcessedData data)
        {
            if (data is not HistogramFrame frame) return;
            if (frame.YMax == _lastYMax) return;

            _lastYMax = frame.YMax;
            histogramPlot.Plot.Axes.SetLimitsY(0, frame.YMax);
            histogramPlot.Refresh();
        }
    }
}
