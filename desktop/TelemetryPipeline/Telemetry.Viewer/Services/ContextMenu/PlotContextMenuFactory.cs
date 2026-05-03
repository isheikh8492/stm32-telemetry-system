using System.Windows.Controls;
using ScottPlot.WPF;

namespace Telemetry.Viewer.Services.ContextMenu;

// Builds the WPF ContextMenu for a plot's right-click menu and attaches it
// to the plot's transparent DragLayer (the topmost hit-testable element on
// each plot). Also clears ScottPlot's own default menu on the WpfPlot so
// our menu is the only one that ever shows.
//
// Rebuilding entries on Opened means every right-click reflects the
// current PlotSettings — labels and actions stay live as the user edits.
public static class PlotContextMenuFactory
{
    public static void Attach(WpfPlot plot, Border dragLayer, Func<IReadOnlyList<ContextMenuProvider>> contextMenuProvider)
    {
        plot.Menu = null;  // suppress ScottPlot's default right-click menu

        var menu = new System.Windows.Controls.ContextMenu();
        menu.Opened += (_, _) =>
        {
            menu.Items.Clear();
            foreach (var entry in contextMenuProvider())
            {
                var item = new MenuItem { Header = entry.Label };
                var captured = entry;
                item.Click += (_, _) => captured.OnInvoke();
                menu.Items.Add(item);
            }
        };
        dragLayer.ContextMenu = menu;
    }
}
