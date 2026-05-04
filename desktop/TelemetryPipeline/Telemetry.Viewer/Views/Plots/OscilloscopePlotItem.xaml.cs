using ScottPlot.WPF;
using Telemetry.Viewer.Views.Plots.Axes;
using Telemetry.Viewer.Views.Worksheet;

namespace Telemetry.Viewer.Views.Plots
{
    public partial class OscilloscopePlotItem : PlotItem
    {
        public OscilloscopePlotItem() => InitializeComponent();

        protected override WpfPlot Plot => oscilloscopePlot;

        protected override void OnApplySettings()
        {
            oscilloscopePlot.Plot.Axes.Rules.Clear();
            oscilloscopePlot.Plot.Axes.Rules.Add(new ScottPlot.AxisRules.LockedVertical(oscilloscopePlot.Plot.Axes.Left, 0, 5000));
            oscilloscopePlot.Plot.Axes.Rules.Add(new ScottPlot.AxisRules.LockedHorizontal(oscilloscopePlot.Plot.Axes.Bottom, 0, 32));

            oscilloscopePlot.Plot.Axes.Left.TickGenerator   = OscilloscopeTickGenerator.BuildY();
            oscilloscopePlot.Plot.Axes.Bottom.TickGenerator = OscilloscopeTickGenerator.BuildX();

            oscilloscopePlot.Plot.XLabel("Window (s)");
            oscilloscopePlot.Plot.YLabel("ADC");
            oscilloscopePlot.Plot.Axes.SetLimits(left: 0, right: 32, bottom: 0, top: 5000);
        }
    }
}
