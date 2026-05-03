using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Telemetry.Viewer.Views.Worksheet;

internal static class PlotContainerFactory
{
    // Builds the visual tree for one plot at the requested worksheet
    // position. Caller is responsible for adding container.Outer to the
    // worksheet canvas.
    public static PlotContainer Create(UIElement plotView, Size defaultSize, Point initialPosition)
    {
        var outer = new Canvas
        {
            Width = defaultSize.Width,
            Height = defaultSize.Height
        };

        var host = new Grid
        {
            Width = defaultSize.Width,
            Height = defaultSize.Height
        };

        if (plotView is FrameworkElement fe)
        {
            fe.Width = defaultSize.Width;
            fe.Height = defaultSize.Height;
        }

        host.Children.Add(plotView);
        Panel.SetZIndex(plotView, 0);

        var dragLayer = new Border
        {
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeAll
        };
        host.Children.Add(dragLayer);
        Panel.SetZIndex(dragLayer, 1);

        outer.Children.Add(host);

        Canvas.SetLeft(outer, initialPosition.X);
        Canvas.SetTop(outer,  initialPosition.Y);

        return new PlotContainer(outer, host, dragLayer);
    }
}
