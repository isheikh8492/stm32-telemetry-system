using System.Windows.Media;
using ScottPlot.WPF;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.ContextMenu;
using Telemetry.Viewer.Views.Plots.Axes;
using Telemetry.Viewer.Views.Worksheet;

namespace Telemetry.Viewer.Views.Plots
{
    public partial class OscilloscopePlotItem : PlotItem
    {
        public OscilloscopePlotItem() => InitializeComponent();

        protected override WpfPlot Plot => oscilloscopePlot;

        private OscilloscopeSettings Osc => (OscilloscopeSettings)DataContext;

        protected override void ApplySettings()
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
