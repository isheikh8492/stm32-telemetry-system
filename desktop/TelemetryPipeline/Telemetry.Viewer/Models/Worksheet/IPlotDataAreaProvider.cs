using System.Windows;

namespace Telemetry.Viewer.Models.Worksheet;

// Optional companion to IPlotView. A plot that knows where its data area
// lives (axes' inside rect) raises this after each render so external
// observers — like ThumbManager — can position themselves on the data
// border instead of the outer container corners.
//
// Rect is in DIPs, in the plot UserControl's own coordinate space.
public interface IPlotDataAreaProvider
{
    event Action<Rect>? DataAreaChanged;
}
