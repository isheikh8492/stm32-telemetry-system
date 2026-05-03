using System.Windows.Controls;

namespace Telemetry.Viewer.Views.Worksheet;

// Visual wrapper around a single plot on the worksheet canvas.
//   Outer    — the Canvas that gets positioned via Canvas.SetLeft/Top on the
//              worksheet. Children: Host + corner thumbs.
//   Host     — Grid that stacks the plot UserControl and the transparent
//              DragLayer. Plot at z=0, DragLayer at z=1.
//   DragLayer — transparent Border with SizeAll cursor; intercepts pointer
//              for drag-to-move so the plot's own input is suppressed.
internal sealed record PlotContainer(Canvas Outer, Grid Host, Border DragLayer);
