using System.Windows;
using System.Windows.Controls;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Worksheet;
using Telemetry.Viewer.Services.ContextMenu;

namespace Telemetry.Viewer.Views.Worksheet;

// Abstract base for everything that can live on the worksheet.
//
// Per-frame UI work is intentionally tiny: the PlotProcessor has already
// painted the bitmap off-thread. Render() runs on the UI thread and:
//   1. lets subclasses do any UI-only work (e.g. axis-range updates that
//      drive ScottPlot's static layer) via OnRender(),
//   2. blits the processor's pixel buffer onto DynamicBitmap.
public abstract class PlotItem : UserControl, IWorksheetItem
{
    public PlotSettings Settings => (PlotSettings)DataContext;
    public Guid Id => Settings.PlotId;
    public new string Name => Settings.DisplayName;

    internal PlotContainer? Container { get; set; }
    internal ThumbManager? Thumbs { get; set; }
    internal Rect DataArea { get; set; }

    public abstract void AttachContextMenu(Func<IReadOnlyList<ContextMenuProvider>> contextMenuProvider);

    public event Action<Rect>? DataAreaChanged;
    protected void RaiseDataAreaChanged(Rect rect) => DataAreaChanged?.Invoke(rect);

    // UI-thread hook for subclasses (axis-range updates, ScottPlot Refresh).
    // Default: no-op. Plots without UI-only state don't need to override.
    protected virtual void OnRender(ProcessedData data) { }

    // Final orchestrator — RenderingEngine calls this on the UI thread.
    public void Render(ProcessedData data)
    {
        if (Container is null) return;
        OnRender(data);
        Container.DynamicSurface.PresentBitmap(data.Buffer, data.PixelWidth, data.PixelHeight);
    }
}
