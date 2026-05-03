using System.Windows;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.ContextMenu;
using Telemetry.Viewer.Views.Dialogs;

namespace Telemetry.Viewer.Views.Plots;

// Histogram plot type's app-level wiring: toolbar label, default size,
// view factory, settings factory, menu shape, dialog routing.
internal static class HistogramPlot
{
    public static void Register(Worksheet.Worksheet worksheet)
    {
        worksheet.RegisterPlotType<HistogramSettings, HistogramPlotItem>(
            type:           PlotType.Histogram,
            label:          "Histogram",
            defaultSize:    new Size(360, 240),
            createSettings: () => new HistogramSettings(
                plotId:    Guid.NewGuid(),
                channelId: 0,
                param:     ParamType.PeakHeight,
                binCount:  64,
                minRange:  1,
                maxRange:  1_000_000,
                scale:     AxisScale.Logarithmic),
            createItem:     () => new HistogramPlotItem(),
            menuBuilder:    s => new[]
            {
                new ContextMenuProvider("Properties...", () => OpenProperties(s))
            });
    }

    private static void OpenProperties(HistogramSettings settings)
    {
        var dialog = new HistogramPropertiesDialog(settings)
        {
            Owner = Application.Current.MainWindow
        };
        dialog.ShowDialog();
    }
}
