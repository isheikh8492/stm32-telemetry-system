using ScottPlot.WPF;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Views.Plots.Axes;
using Telemetry.Viewer.Views.Worksheet;

namespace Telemetry.Viewer.Views.Plots
{
    public partial class PseudocolorPlotItem : PlotItem
    {
        public PseudocolorPlotItem() => InitializeComponent();

        protected override WpfPlot Plot => pseudocolorPlot;

        private PseudocolorSettings Pc => (PseudocolorSettings)DataContext;

        // Both axes run in bin-position space (0..BinCount); AxisItem helpers
        // place labels at data-equivalent positions for each axis's scale.
        protected override void OnApplySettings()
        {
            pseudocolorPlot.Plot.XLabel(Pc.XSelection.Label);
            pseudocolorPlot.Plot.YLabel(Pc.YSelection.Label);

            var bins = (int)Pc.BinCount;
            var xBins = bins;
            var yBins = bins;

            pseudocolorPlot.Plot.Axes.Bottom.TickGenerator =
                AxisFactory.For(Pc.XScale).BuildBinTickGenerator(Pc.XMinRange, Pc.XMaxRange, xBins);
            pseudocolorPlot.Plot.Axes.Left.TickGenerator =
                AxisFactory.For(Pc.YScale).BuildBinTickGenerator(Pc.YMinRange, Pc.YMaxRange, yBins);

            pseudocolorPlot.Plot.Axes.SetLimitsX(0, xBins);
            pseudocolorPlot.Plot.Axes.SetLimitsY(0, yBins);
        }
    }
}
