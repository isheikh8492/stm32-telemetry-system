using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Telemetry.Viewer.Views.Worksheet;

internal static class DragHandler
{
    // Wires drag-to-move on the host's transparent DragLayer. Snaps the
    // DATA RECT's top-left to grid intersections (not the host's), matching
    // the placement-time alignment so the corner thumbs stay on the grid
    // through every drag. Mutates ViewModel.X/Y; ItemsControl + Canvas.Left
    // bindings translate that into the visual move.
    public static void Wire(PlotItemHost host, Worksheet worksheet)
    {
        Point dragOffset = default;
        bool dragging = false;

        host.DragLayerElement.MouseLeftButtonDown += (_, e) =>
        {
            if (host.ViewModel is null) return;
            worksheet.Select(host.ViewModel);
            dragOffset = e.GetPosition(host);
            host.DragLayerElement.CaptureMouse();
            dragging = true;
            e.Handled = true;
        };

        host.DragLayerElement.MouseMove += (_, e) =>
        {
            if (!dragging || host.ViewModel is null) return;
            var canvas = FindCanvasAncestor(host);
            if (canvas is null) return;

            var p = e.GetPosition(canvas);
            var snap = worksheet.SnapSize;
            var area = host.LastDataArea;

            var rawL = p.X - dragOffset.X;
            var rawT = p.Y - dragOffset.Y;

            // Snap the data rect's TL — not the host's — to a grid intersection,
            // then back out host X/Y.
            var l = Snap(rawL + area.X, snap) - area.X;
            var t = Snap(rawT + area.Y, snap) - area.Y;

            host.ViewModel.X = Math.Max(0, l);
            host.ViewModel.Y = Math.Max(0, t);
        };

        host.DragLayerElement.MouseLeftButtonUp += (_, _) =>
        {
            if (!dragging) return;
            host.DragLayerElement.ReleaseMouseCapture();
            dragging = false;
        };

        host.DragLayerElement.MouseRightButtonDown += (_, _) =>
        {
            if (host.ViewModel is not null)
                worksheet.Select(host.ViewModel);
        };
    }

    // Walks up to the worksheet's Canvas (the ItemsControl's panel) so we can
    // read mouse position in worksheet coordinates.
    private static IInputElement? FindCanvasAncestor(DependencyObject from)
    {
        var obj = VisualTreeHelper.GetParent(from);
        while (obj is not null)
        {
            if (obj is System.Windows.Controls.Canvas c) return c;
            obj = VisualTreeHelper.GetParent(obj);
        }
        return null;
    }

    private static double Snap(double v, double s) => s > 0 ? Math.Round(v / s) * s : v;
}
