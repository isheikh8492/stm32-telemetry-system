using System.Windows;
using System.Windows.Controls;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Worksheet;
using Telemetry.Viewer.Services.ContextMenu;

namespace Telemetry.Viewer.Views.Worksheet;

// Abstract base for everything that can live on the worksheet. Combines
// in one type:
//   * the rendering contract the pipeline calls (Settings, Render)
//   * the menu hook the worksheet calls (AttachContextMenu)
//   * the data-area broadcast (DataAreaChanged) the worksheet uses for grid
//     alignment and ThumbManager uses to position handles
//   * per-placement bookkeeping (Container, Thumbs, DataArea) the worksheet
//     fills in after creation
//
// Each concrete plot type extends this — e.g., OscilloscopePlotItem.
public abstract class PlotItem : UserControl, IWorksheetItem
{
    public PlotSettings Settings => (PlotSettings)DataContext;
    public Guid Id => Settings.PlotId;
    public new string Name => Settings.DisplayName;

    // Worksheet-attached state. Set by the Worksheet right after construction;
    // null only during a brief construction window before the plot lands.
    internal PlotContainer? Container { get; set; }
    internal ThumbManager? Thumbs { get; set; }
    internal Rect DataArea { get; set; }

    public abstract void Render(ProcessedData data);
    public abstract void AttachContextMenu(Func<IReadOnlyList<ContextMenuProvider>> contextMenuProvider);

    public event Action<Rect>? DataAreaChanged;
    protected void RaiseDataAreaChanged(Rect rect) => DataAreaChanged?.Invoke(rect);
}
