using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScottPlot.WPF;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Worksheet;
using Telemetry.Viewer.Services.ContextMenu;

namespace Telemetry.Viewer.Views.Worksheet;

// Abstract base for everything that can live on the worksheet.
//
// Per-frame UI work is intentionally tiny: PlotProcessor has already painted
// the bitmap off-thread. Render() runs on the UI thread and:
//   1. lets subclasses do any UI-only work (e.g. axis-range updates that
//      drive ScottPlot's static layer) via OnRender(),
//   2. blits the processor's pixel buffer onto DynamicBitmap.
//
// Boilerplate that every plot type would otherwise duplicate also lives here:
// settings change → ApplySettings; ScottPlot RenderFinished → broadcast the
// data rect (in DIPs) so the worksheet can size DynamicBitmap to it.
public abstract class PlotItem : UserControl, IWorksheetItem
{
    public PlotSettings Settings => (PlotSettings)DataContext;
    public Guid Id => Settings.PlotId;
    public new string Name => Settings.DisplayName;

    internal PlotContainer? Container { get; set; }
    internal ThumbManager? Thumbs { get; set; }
    internal Rect DataArea { get; set; }

    // Current target pixel size of this plot's data-bitmap surface. Read by
    // ViewportSession when hydrating the DataStore on AddPlot and on every
    // DataAreaChanged. (0,0) until the surface has been Sync'd.
    public int PixelWidth  => Container?.DynamicSurface.TargetWidth  ?? 0;
    public int PixelHeight => Container?.DynamicSurface.TargetHeight ?? 0;

    // The ScottPlot control that owns this plot's static layer (axes/labels).
    // Subclasses point this at their named WpfPlot from XAML.
    protected abstract WpfPlot Plot { get; }

    public abstract void AttachContextMenu(Func<IReadOnlyList<ContextMenuProvider>> contextMenuProvider);

    public event Action<Rect>? DataAreaChanged;
    protected void RaiseDataAreaChanged(Rect rect) => DataAreaChanged?.Invoke(rect);

    protected PlotItem()
    {
        Loaded += OnLoadedBase;
    }

    private void OnLoadedBase(object sender, RoutedEventArgs e)
    {
        // Subscribe BEFORE ApplySettings — ApplySettings's own Refresh() is
        // typically the first render, and we need to catch its RenderFinished
        // to learn the real DataRect (LastRender is bogus until then).
        Plot.Plot.RenderManager.RenderFinished += OnRenderFinished;

        Settings.PropertyChanged += (_, _) => ApplySettings();
        ApplySettings();
    }

    // RenderFinished can fire on a non-UI thread; marshal back to UI dispatcher.
    private void OnRenderFinished(object? sender, ScottPlot.RenderDetails e)
        => Plot.Dispatcher.Invoke(BroadcastDataArea);

    private void BroadcastDataArea()
    {
        var px = Plot.Plot.RenderManager.LastRender.DataRect;
        var dpi = VisualTreeHelper.GetDpi(Plot);
        RaiseDataAreaChanged(new Rect(
            px.Left   / dpi.DpiScaleX,
            px.Top    / dpi.DpiScaleY,
            px.Width  / dpi.DpiScaleX,
            px.Height / dpi.DpiScaleY));
    }

    // Settings-driven scaffolding (axes, labels, ranges). Idempotent — called
    // on Loaded and on every Settings.PropertyChanged.
    protected abstract void ApplySettings();

    // UI-thread hook for subclasses (axis-range updates, ScottPlot Refresh).
    protected virtual void OnRender(ProcessedData data) { }

    // Final orchestrator — RenderingEngine calls this on the UI thread.
    public void Render(ProcessedData data)
    {
        if (Container is null) return;
        OnRender(data);
        Container.DynamicSurface.PresentBitmap(data.Buffer, data.PixelWidth, data.PixelHeight);
    }
}
