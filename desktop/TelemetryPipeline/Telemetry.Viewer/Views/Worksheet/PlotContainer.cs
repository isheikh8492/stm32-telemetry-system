using System.Windows.Controls;
using Telemetry.Viewer.Views.Plots.DynamicSurface;

namespace Telemetry.Viewer.Views.Worksheet;

// Visual wrapper around a single plot on the worksheet canvas.
//   Outer          — Canvas positioned via Canvas.SetLeft/Top on the worksheet.
//                    Children: Host + corner thumbs.
//   Host           — Grid stacking the plot, dynamic surface, and drag layer.
//                    z=0 plot (axes/labels/static); z=1 dynamic bitmap (data);
//                    z=2 drag layer (transparent, captures pointer).
//   DragLayer      — transparent Border with SizeAll cursor; intercepts pointer
//                    for drag-to-move so the plot's own input is suppressed.
//   DynamicSurface — WriteableBitmap layered over the WpfPlot's data rect.
//                    Plot views write per-frame data here directly; ScottPlot
//                    only renders the static layer (and only on settings or
//                    Y-range changes), saving Refresh() cost at high event rates.
internal sealed record PlotContainer(
    Canvas Outer,
    Grid Host,
    Border DragLayer,
    DynamicBitmap DynamicSurface);
