using System.Windows;
using System.Windows.Controls;

namespace Telemetry.Viewer.Views.Worksheet;

internal static class DragHandler
{
    // Wires drag-to-move on the container's transparent DragLayer. Snaps the
    // DATA RECT's top-left to grid intersections (not the outer container's),
    // matching the placement-time alignment so corner thumbs stay on the grid
    // through every drag.
    public static void Wire(
        PlotItem item,
        Canvas worksheet,
        Action<PlotItem> onSelect,
        Func<double> getSnapSize)
    {
        var container = item.Container!;
        Point dragOffset = default;
        bool dragging = false;

        container.DragLayer.MouseLeftButtonDown += (_, e) =>
        {
            onSelect(item);
            dragOffset = e.GetPosition(container.Outer);
            container.DragLayer.CaptureMouse();
            dragging = true;
            e.Handled = true;
        };

        container.DragLayer.MouseMove += (_, e) =>
        {
            if (!dragging) return;
            var p = e.GetPosition(worksheet);
            var snap = getSnapSize();
            var area = item.DataArea;

            var rawL = p.X - dragOffset.X;
            var rawT = p.Y - dragOffset.Y;

            // Snap the data rect's TL — not the outer's — to a grid
            // intersection, then back out outer.L/T.
            var l = Snap(rawL + area.X, snap) - area.X;
            var t = Snap(rawT + area.Y, snap) - area.Y;

            Canvas.SetLeft(container.Outer, Math.Max(0, l));
            Canvas.SetTop(container.Outer,  Math.Max(0, t));
        };

        container.DragLayer.MouseLeftButtonUp += (_, _) =>
        {
            if (!dragging) return;
            container.DragLayer.ReleaseMouseCapture();
            dragging = false;
        };

        container.DragLayer.MouseRightButtonDown += (_, _) => onSelect(item);
    }

    private static double Snap(double v, double s) => s > 0 ? Math.Round(v / s) * s : v;
}
