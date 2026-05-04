using System.Windows;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.ContextMenu;
using Telemetry.Viewer.Services.Dialogs;
using Telemetry.Viewer.Services.Pipeline.Processors;
using Telemetry.Viewer.Views.Dialogs;

namespace Telemetry.Viewer.Views.Plots;

// Oscilloscope plot type's app-level wiring: toolbar label, default size,
// view factory, settings factory, menu shape, dialog routing. One file
// owns everything plot-type-specific. New plot types follow the same shape.
internal static class OscilloscopePlot
{
    public static void Register(Worksheet.Worksheet worksheet, IDialogService dialogs)
    {
        PlotProcessorRegistry.Register(PlotType.Oscilloscope, new OscilloscopePlotProcessor());

        worksheet.RegisterPlotType<OscilloscopeSettings, OscilloscopePlotItem>(
            type:           PlotType.Oscilloscope,
            label:          "Oscilloscope",
            defaultSize:    new Size(280, 160),
            createSettings: () => new OscilloscopeSettings(plotId: Guid.NewGuid(), channelId: 0),
            createItem:     () => new OscilloscopePlotItem(),
            menuBuilder:    s => new[]
            {
                new ContextMenuProvider("Properties...", () => OpenProperties(s, dialogs))
            });
    }

    private static void OpenProperties(OscilloscopeSettings settings, IDialogService dialogs)
        => dialogs.ShowDialog(new OscilloscopePropertiesDialog(settings));
}
