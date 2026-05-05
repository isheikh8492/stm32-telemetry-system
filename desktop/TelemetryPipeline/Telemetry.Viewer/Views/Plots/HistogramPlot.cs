using System.Windows;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.ContextMenu;
using Telemetry.Viewer.Services.Dialogs;
using Telemetry.Viewer.Services.Pipeline.Processors;
using Telemetry.Viewer.Views.Dialogs;

namespace Telemetry.Viewer.Views.Plots;

// Histogram plot type's app-level wiring: toolbar label, default size,
// view factory, settings factory, menu shape, dialog routing.
internal static class HistogramPlot
{
    public static void Register(Worksheet.Worksheet worksheet, IDialogService dialogs)
    {
        PlotProcessorRegistry.Register(PlotType.Histogram, new HistogramPlotProcessor());

        worksheet.RegisterPlotType<HistogramSettings, HistogramPlotItem>(
            type:           PlotType.Histogram,
            label:          "Histogram",
            defaultSize:    new Size(220, 160),
            createSettings: () => new HistogramSettings(
                plotId:    Guid.NewGuid(),
                channelId: 0,
                param:     ParamType.PeakHeight,
                binCount:  BinCount.Bins256,
                minRange:  1,
                maxRange:  1_000_000,
                scale:     AxisScale.Logarithmic),
            createItem:     () => new HistogramPlotItem(),
            menuBuilder:    s => new[]
            {
                new ContextMenuProvider("Properties...", () => OpenProperties(s, dialogs))
            });
    }

    private static void OpenProperties(HistogramSettings settings, IDialogService dialogs)
        => dialogs.ShowDialog(new HistogramPropertiesDialog(settings));
}
