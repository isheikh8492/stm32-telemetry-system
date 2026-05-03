using System.Windows;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.ContextMenu;
using Telemetry.Viewer.Views.Dialogs;

namespace Telemetry.Viewer.Views.Plots;

// Oscilloscope plot type's app-level wiring: toolbar label, default size,
// view factory, settings factory, menu shape, dialog routing. One file
// owns everything plot-type-specific. Worksheet calls Register(this) once
// in its ctor; new plot types follow the same shape.
internal static class OscilloscopePlot
{
    public static void Register(Worksheet.Worksheet worksheet)
    {
        worksheet.RegisterPlotType<OscilloscopeSettings, OscilloscopePlotItem>(
            label:          "Oscilloscope",
            defaultSize:    new Size(300, 200),
            createSettings: () => new OscilloscopeSettings(plotId: Guid.NewGuid(), channelId: 0),
            createItem:     () => new OscilloscopePlotItem(),
            menuBuilder:    s => new[]
            {
                new ContextMenuProvider("Properties...", () => OpenProperties(s))
            });
    }

    private static void OpenProperties(OscilloscopeSettings settings)
    {
        var dialog = new OscilloscopePropertiesDialog(settings)
        {
            Owner = Application.Current.MainWindow
        };
        dialog.ShowDialog();
    }
}
