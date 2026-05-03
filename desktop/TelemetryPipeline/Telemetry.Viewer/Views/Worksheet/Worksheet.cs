using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Telemetry.Viewer.Common;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Worksheet;
using Telemetry.Viewer.Services.ContextMenu;
using Telemetry.Viewer.Services.Pipeline;
using Telemetry.Viewer.Views.Plots;

namespace Telemetry.Viewer.Views.Worksheet;

public sealed record PlotTypeOption(string Label, ICommand AddCommand);

// App-lifetime owner of "what's on the worksheet."
//
// Workflow: clicking an Add-<X> toolbar button arms placement mode (cursor
// becomes a crosshair); the next click on the worksheet canvas drops the
// plot at that point. After the first render we adjust outer.L/T so the
// data rect's TL lands on the snapped click, and resize the outer so the
// data rect's W/H are integer multiples of snap — every corner thumb starts
// on a grid intersection.
public sealed class Worksheet
{
    public ObservableCollection<PlotTypeOption> PlotTypes { get; } = new();

    private readonly Dictionary<Guid, PlotItem> _plots = new();
    private readonly Dictionary<Type, Func<PlotSettings, IReadOnlyList<ContextMenuProvider>>> _menus = new();
    private readonly Dictionary<Type, Func<IPlotView>> _viewFactories = new();
    private readonly Dictionary<Type, Size> _defaultSizes = new();

    private readonly SelectionManager<PlotItem> _selection = new();

    private Canvas? _canvas;
    private ViewportSession? _session;
    private Func<PlotSettings>? _pendingFactory;

    private double _snapSize = 40;
    private int _nextZIndex = 1;

    public Worksheet()
    {
        OscilloscopePlot.Register(this);
        // future: HistogramPlot.Register(this);

        _selection.SelectionChanged += item =>
        {
            if (item is not null)
                Panel.SetZIndex(item.Container.Outer, _nextZIndex++);
        };
    }

    // ---- Plot-type registration (called from each <X>Plot.Register) ----

    public void RegisterPlotType<TSettings, TView>(
        string label,
        Size defaultSize,
        Func<TSettings> createSettings,
        Func<TView> createView,
        Func<TSettings, IReadOnlyList<ContextMenuProvider>> menuBuilder)
        where TSettings : PlotSettings
        where TView : UIElement, IPlotView
    {
        _menus[typeof(TSettings)] = s => menuBuilder((TSettings)s);
        _viewFactories[typeof(TSettings)] = () => createView();
        _defaultSizes[typeof(TSettings)] = defaultSize;

        PlotTypes.Add(new PlotTypeOption(
            Label: label,
            AddCommand: new RelayCommand(() => StartPlacement(createSettings))));
    }

    private IReadOnlyList<ContextMenuProvider> GetMenuFor(PlotSettings settings)
        => _menus.TryGetValue(settings.GetType(), out var b) ? b(settings) : Array.Empty<ContextMenuProvider>();

    // ---- Canvas attachment (called by WorksheetGrid on Loaded) ----

    public void AttachCanvas(Canvas canvas)
    {
        _canvas = canvas;
        canvas.MouseLeftButtonDown += OnCanvasClick;
    }

    private void OnCanvasClick(object sender, MouseButtonEventArgs e)
    {
        if (_pendingFactory is null)
        {
            _selection.Select(null);
            return;
        }

        var pos = e.GetPosition(_canvas);
        var settings = _pendingFactory();
        EndPlacement();
        AddPlotAt(settings, pos);
        e.Handled = true;
    }

    // ---- Placement mode ----

    private void StartPlacement(Func<PlotSettings> factory)
    {
        _pendingFactory = () => factory();
        if (_canvas is not null)
            _canvas.Cursor = Cursors.Cross;
    }

    private void EndPlacement()
    {
        _pendingFactory = null;
        if (_canvas is not null)
            _canvas.Cursor = null;
    }

    // ---- Plot lifecycle ----

    private void AddPlotAt(PlotSettings settings, Point worksheetPoint)
    {
        if (_canvas is null) return;
        if (!_viewFactories.TryGetValue(settings.GetType(), out var viewFactory)) return;

        var view = viewFactory();
        if (view is FrameworkElement fe)
            fe.DataContext = settings;

        var size = _defaultSizes.TryGetValue(settings.GetType(), out var ds) ? ds : new Size(400, 200);

        // Initial position: snap click point to nearest grid intersection.
        // After the first render we'll adjust outer.L/T so the data area's TL
        // (not the outer's TL) lands exactly on the intersection.
        var snap = _snapSize;
        var snappedX = Snap(worksheetPoint.X, snap);
        var snappedY = Snap(worksheetPoint.Y, snap);

        var container = PlotContainerFactory.Create((UIElement)view, size, new Point(snappedX, snappedY));
        view.AttachContextMenu(() => GetMenuFor(view.Settings));

        var item = new PlotItem(settings, view, container);
        _plots[settings.PlotId] = item;

        var thumbs = ThumbManager.Wire(container, view, () => _snapSize);
        DragHandler.Wire(container, item, _canvas, _selection, () => _snapSize);
        _selection.Register(item, onSelect: thumbs.Show, onDeselect: thumbs.Hide);

        _canvas.Children.Add(container.Outer);
        _selection.Select(item);

        if (view is IPlotDataAreaProvider provider)
        {
            // Cache the latest data-area rect on the item so DragHandler can
            // snap the data rect's TL (not the outer's TL) to grid intersections.
            provider.DataAreaChanged += rect => item.DataArea = rect;

            // First-render alignment: data rect's TL → clicked intersection,
            // data rect's W/H → integer multiples of snap. Resizing the plot
            // reflows axis labels and shifts the next render's data rect, so
            // iterate a few rounds. Each pass re-reads the current data area;
            // if chrome is stable, later rounds are no-ops.
            int iter = 0;
            Action<Rect>? handler = null;
            handler = rect =>
            {
                if (++iter > 6)
                {
                    provider.DataAreaChanged -= handler;
                    return;
                }
                AlignToGrid(container, rect, snappedX, snappedY, snap);
            };
            provider.DataAreaChanged += handler;
        }

        _session?.AddPlot(view);
    }

    // After first render: position so data area's TL = clicked intersection,
    // and resize so data area's W/H are integer multiples of snap (every
    // corner thumb lands on an intersection).
    private static void AlignToGrid(PlotContainer container, Rect dataArea, double targetTLx, double targetTLy, double snap)
    {
        Canvas.SetLeft(container.Outer, targetTLx - dataArea.X);
        Canvas.SetTop(container.Outer,  targetTLy - dataArea.Y);

        var leftChrome   = dataArea.X;
        var topChrome    = dataArea.Y;
        var rightChrome  = container.Outer.Width  - dataArea.Right;
        var bottomChrome = container.Outer.Height - dataArea.Bottom;

        var snappedDataW = Math.Max(snap, Math.Round(dataArea.Width  / snap) * snap);
        var snappedDataH = Math.Max(snap, Math.Round(dataArea.Height / snap) * snap);

        var newW = snappedDataW + leftChrome + rightChrome;
        var newH = snappedDataH + topChrome  + bottomChrome;

        container.Outer.Width  = newW;
        container.Outer.Height = newH;
        container.Host.Width   = newW;
        container.Host.Height  = newH;

        if (container.Host.Children.Count > 0 && container.Host.Children[0] is FrameworkElement plot)
        {
            plot.Width = newW;
            plot.Height = newH;
        }
    }

    public void RemovePlot(Guid plotId)
    {
        if (!_plots.TryGetValue(plotId, out var item)) return;

        _selection.Unregister(item);
        _canvas?.Children.Remove(item.Container.Outer);
        _plots.Remove(plotId);
        _session?.RemovePlot(plotId);
    }

    // ---- Session binding ----

    public void BindSession(ViewportSession session)
    {
        _session = session;
        foreach (var item in _plots.Values)
            session.AddPlot(item.View);
    }

    public void UnbindSession() => _session = null;

    private static double Snap(double v, double s) => s > 0 ? Math.Round(v / s) * s : v;
}
