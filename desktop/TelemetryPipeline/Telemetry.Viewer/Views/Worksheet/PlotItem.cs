using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScottPlot.WPF;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Services.ContextMenu;
using Telemetry.Viewer.Services.Pipeline;

namespace Telemetry.Viewer.Views.Worksheet;

// Abstract base for everything that can live on the worksheet.
//
// Per-frame UI work is intentionally tiny: PlotProcessor has already painted
// the bitmap off-thread. Render() runs on the UI thread, lets subclasses do
// any UI-only work via OnRender(), then forwards the buffer to the host's
// DynamicBitmap for a memcpy blit.
//
// Boilerplate every plot type would otherwise duplicate also lives here:
// settings change → ApplySettings; ScottPlot RenderFinished → broadcast the
// data rect (in DIPs) so PlotItemHost can size DynamicBitmap to it.
public abstract class PlotItem : UserControl, IRenderTarget
{
    public PlotSettings Settings => (PlotSettings)DataContext;
    public Guid Id => Settings.PlotId;
    public new string Name => Settings.DisplayName;

    // Set by PlotItemHost during its Loaded handler. Render() forwards the
    // painted buffer here; PixelWidth/Height read from the host's bitmap.
    internal PlotItemHost? Host { get; set; }

    public int PixelWidth  => Host?.DataLayerElement.TargetWidth  ?? 0;
    public int PixelHeight => Host?.DataLayerElement.TargetHeight ?? 0;

    // The ScottPlot control that owns this plot's static layer (axes/labels).
    // Subclasses point this at their named WpfPlot from XAML.
    protected abstract WpfPlot Plot { get; }

    public event Action<Rect>? DataAreaChanged;
    protected void RaiseDataAreaChanged(Rect rect) => DataAreaChanged?.Invoke(rect);

    private PropertyChangedEventHandler? _settingsHandler;
    private EventHandler<ScottPlot.RenderDetails>? _renderFinishedHandler;
    private PlotSettings? _subscribedSettings;

    protected PlotItem()
    {
        Loaded   += OnLoadedBase;
        Unloaded += OnUnloadedBase;
    }

    private void OnLoadedBase(object sender, RoutedEventArgs e)
    {
        // Subscribe BEFORE ApplySettings — its own Refresh() is typically the
        // first render, and we need to catch its RenderFinished to learn the
        // real DataRect (LastRender is bogus until then).
        _renderFinishedHandler = OnRenderFinished;
        Plot.Plot.RenderManager.RenderFinished += _renderFinishedHandler;

        _subscribedSettings = Settings;
        _settingsHandler = (_, _) => ApplySettings();
        _subscribedSettings.PropertyChanged += _settingsHandler;

        ApplySettings();
    }

    // Template method: clears the plot, applies the worksheet's standard
    // chrome (transparent figure / white data background, hidden grid), then
    // delegates to the subclass's OnApplySettings for axes/labels/limits.
    // Refresh once at the end so subclasses don't each remember to call it.
    private void ApplySettings()
    {
        Plot.Plot.Clear();
        Plot.Background = Brushes.Transparent;
        Plot.Plot.FigureBackground.Color = ScottPlot.Colors.Transparent;
        Plot.Plot.DataBackground.Color   = ScottPlot.Colors.White;
        Plot.Plot.Grid.IsVisible = false;

        OnApplySettings();
        Plot.Refresh();
    }

    private void OnUnloadedBase(object sender, RoutedEventArgs e)
    {
        if (_renderFinishedHandler is not null)
            Plot.Plot.RenderManager.RenderFinished -= _renderFinishedHandler;
        _renderFinishedHandler = null;

        if (_subscribedSettings is not null && _settingsHandler is not null)
            _subscribedSettings.PropertyChanged -= _settingsHandler;
        _subscribedSettings = null;
        _settingsHandler = null;
    }

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

    // Type-specific scaffolding — axes, tick generators, labels, ranges.
    // Common chrome (Clear, backgrounds, grid) and the trailing Refresh()
    // are handled by ApplySettings before/after this runs.
    protected abstract void OnApplySettings();

    protected virtual void OnRender(ProcessedData data) { }

    // Final orchestrator — RenderingEngine calls this on the UI thread.
    public void Render(ProcessedData data)
    {
        if (Host is null) return;
        OnRender(data);
        if (data.IsEmpty)
            Host.DataLayerElement.Clear();
        else
            Host.DataLayerElement.PresentBitmap(data.Buffer, data.PixelWidth, data.PixelHeight);
    }

    public void Clear() => Host?.DataLayerElement.Clear();

    // Plot exposes the ScottPlot control already; the host owns the drag
    // overlay that holds the right-click handler. Called by PlotItemHost
    // once it's in the visual tree and has built its tree.
    public void AttachContextMenu(Func<IReadOnlyList<ContextMenuProvider>> contextMenuProvider)
    {
        if (Host is null) return;
        PlotContextMenuFactory.Attach(Plot, Host.DragLayerElement, contextMenuProvider);
    }
}
