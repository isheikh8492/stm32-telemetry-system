using System.Windows;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.ContextMenu;
using Telemetry.Viewer.Services.Dialogs;
using Telemetry.Viewer.Services.Pipeline.Processors;
using Telemetry.Viewer.Views.Dialogs;

namespace Telemetry.Viewer.Views.Plots;

// Pseudocolor (2D heatmap) plot type's app-level wiring.
internal static class PseudocolorPlot
{
    public static void Register(Worksheet.Worksheet worksheet, IDialogService dialogs)
    {
        PlotProcessorRegistry.Register(PlotType.Pseudocolor, new PseudocolorPlotProcessor());

        worksheet.RegisterPlotType<PseudocolorSettings, PseudocolorPlotItem>(
            type:           PlotType.Pseudocolor,
            label:          "Pseudocolor",
            defaultSize:    new Size(220, 220),
            createSettings: () => new PseudocolorSettings(
                plotId:    Guid.NewGuid(),
                xChannelId: 0, xParam: ParamType.PeakHeight,
                yChannelId: 1, yParam: ParamType.PeakHeight,
                binCount:  BinCount.Bins256,
                xMinRange:  1, xMaxRange: 1_000_000,
                yMinRange:  1, yMaxRange: 1_000_000,
                xScale:     AxisScale.Logarithmic,
                yScale:     AxisScale.Logarithmic),
            createItem:     () => new PseudocolorPlotItem(),
            menuBuilder:    s => new[]
            {
                new ContextMenuProvider("Properties...", () => OpenProperties(s, dialogs))
            });
    }

    private static void OpenProperties(PseudocolorSettings settings, IDialogService dialogs)
        => dialogs.ShowDialog(new PseudocolorPropertiesDialog(settings));
}
