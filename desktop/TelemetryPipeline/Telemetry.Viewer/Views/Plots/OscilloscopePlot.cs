using System.Windows;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.ContextMenu;
using Telemetry.Viewer.Views.Dialogs;

namespace Telemetry.Viewer.Views.Plots;

// Oscilloscope plot type's app-level wiring: toolbar label + factory, menu
// shape, and Properties dialog routing. Worksheet calls Register(this) once
// at construction; new plot types follow the same shape.
internal static class OscilloscopePlot
{
    public static void Register(Worksheet.Worksheet worksheet)
    {
        worksheet.RegisterPlotType<OscilloscopeSettings>(
            label: "Oscilloscope",
            createSettings: () => new OscilloscopeSettings(plotId: Guid.NewGuid(), channelId: 0),
            menuBuilder: s => new[]
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
