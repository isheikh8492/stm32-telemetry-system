using ScottPlot;
using ScottPlot.TickGenerators;
using ScottPlot.WPF;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Views.Plots.Axes;
using Telemetry.Viewer.Views.Worksheet;

namespace Telemetry.Viewer.Views.Plots
{
    public partial class SpectralRibbonPlotItem : PlotItem
    {
        public SpectralRibbonPlotItem() => InitializeComponent();

        protected override WpfPlot Plot => ribbonPlot;

        private SpectralRibbonSettings Sr => (SpectralRibbonSettings)DataContext;

        // X axis: discrete channels, one column each. Ticks land at column
        // centers (i + 0.5) with the channel's display name.
        // Y axis: bin-position space (0..BinCount); the AxisItem helper
        // places labels at data-equivalent positions for the chosen scale.
        protected override void OnApplySettings()
        {
            ribbonPlot.Plot.YLabel(Sr.Param.ToString());

            var channelCount = Sr.ChannelIds.Count;
            var xTicks = new Tick[channelCount];
            for (int i = 0; i < channelCount; i++)
            {
                var name = SelectionStrategy.GetChannel(Sr.ChannelIds[i]).Name;
                xTicks[i] = new Tick(i + 0.5, name, true);
            }
            ribbonPlot.Plot.Axes.Bottom.TickGenerator = new NumericManual(xTicks);
            // Channel names get long fast — rotate 90° so they read vertically
            // and the column stays narrow enough to fit lots of channels.
            // MiddleLeft anchors the start of the text at the tick so the
            // rest of the label hangs below the axis line.
            ribbonPlot.Plot.Axes.Bottom.TickLabelStyle.Rotation = 90;
            ribbonPlot.Plot.Axes.Bottom.TickLabelStyle.Alignment = ScottPlot.Alignment.MiddleLeft;
            // Default tick-to-label gap is consumed by the rotation anchor;
            // push labels down a few pixels so they don't kiss the tick marks
            // (matches the Y-axis's natural tick-label gap).
            ribbonPlot.Plot.Axes.Bottom.TickLabelStyle.OffsetY = 6;
            // Auto-size doesn't measure rotated labels properly — reserve
            // height for the longest channel name.
            ribbonPlot.Plot.Axes.Bottom.MinimumSize = 60;

            var bins = (int)Sr.BinCount;
            ribbonPlot.Plot.Axes.Left.TickGenerator =
                AxisFactory.For(Sr.Scale).BuildBinTickGenerator(Sr.MinRange, Sr.MaxRange, bins);

            ribbonPlot.Plot.Axes.SetLimitsX(0, channelCount);
            ribbonPlot.Plot.Axes.SetLimitsY(0, bins);
        }
    }
}
