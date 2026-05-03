using ScottPlot.WPF;

namespace Telemetry.Viewer.Services.ContextMenu;

// Builds the WPF ContextMenu that backs every plot view's right-click menu.
// Each IPlotView delegates AttachContextMenu here so the WPF/ScottPlot
// plumbing — disabling the default menu, rebuilding entries on Opened so
// they always reflect current settings — lives in one place.
public static class PlotContextMenuFactory
{
    public static void Attach(WpfPlot host, Func<IReadOnlyList<ContextMenuProvider>> contextMenuProvider)
    {
        host.Menu = null;  // suppress ScottPlot's default right-click menu

        var menu = new System.Windows.Controls.ContextMenu();
        menu.Opened += (_, _) =>
        {
            menu.Items.Clear();
            foreach (var entry in contextMenuProvider())
            {
                var item = new System.Windows.Controls.MenuItem { Header = entry.Label };
                var captured = entry;
                item.Click += (_, _) => captured.OnInvoke();
                menu.Items.Add(item);
            }
        };
        host.ContextMenu = menu;
    }
}
