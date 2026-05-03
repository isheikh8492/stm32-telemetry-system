using System.Windows;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.ContextMenu;
using Telemetry.Viewer.Views.Dialogs;

namespace Telemetry.Viewer.Views.Plots;

// Oscilloscope plot type's app-level wiring: the right-click menu shape
// and the Properties dialog routing. Worksheet calls Register(this) once
// at construction; new plot types follow the same shape.
internal static class OscilloscopePlot
{
    public static void Register(Worksheet.Worksheet worksheet)
    {
        worksheet.RegisterMenu<OscilloscopeSettings>(s => new[]
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
