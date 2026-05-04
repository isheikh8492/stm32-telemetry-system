using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Telemetry.Viewer.Common;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.ContextMenu;
using Telemetry.Viewer.Services.Pipeline;
using Telemetry.Viewer.Views.Plots;
using Telemetry.Viewer.Views.Plots.DynamicSurface;

namespace Telemetry.Viewer.Views.Worksheet;

public sealed record PlotTypeOption(string Label, ICommand AddCommand);

// App-lifetime owner of "what's on the worksheet."
//
// Workflow: clicking an Add-<X> toolbar button arms placement mode (cursor
// becomes a crosshair); the next click on the worksheet canvas drops the
// plot at that point. After the first render we adjust the outer rect so
// the data rect's TL lands on the snapped click, and resize so the data
// rect's W/H are integer multiples of snap — every corner thumb starts
// on a grid intersection. Drag/resize preserve that invariant by snapping
// the data rect (not the outer) at every step.
public sealed class Worksheet
{
    public ObservableCollection<PlotTypeOption> PlotTypes { get; } = new();

    private readonly Dictionary<Guid, PlotItem> _plots = new();
    private readonly Dictionary<PlotType, Func<PlotSettings, IReadOnlyList<ContextMenuProvider>>> _menus = new();
    private readonly Dictionary<PlotType, Func<PlotItem>> _itemFactories = new();
    private readonly Dictionary<PlotType, Size> _defaultSizes = new();

    private Canvas? _canvas;
    private ViewportSession? _session;
    private Func<PlotSettings>? _pendingFactory;
    private PlotItem? _selected;

    private double _snapSize = 40;
    private int _nextZIndex = 1;

    public Worksheet()
    {
        OscilloscopePlot.Register(this);
        HistogramPlot.Register(this);
    }

    // ---- Plot-type registration (called from each <X>Plot.Register) ----

    public void RegisterPlotType<TSettings, TItem>(
        PlotType type,
        string label,
        Size defaultSize,
        Func<TSettings> createSettings,
        Func<TItem> createItem,
        Func<TSettings, IReadOnlyList<ContextMenuProvider>> menuBuilder)
        where TSettings : PlotSettings
        where TItem : PlotItem
    {
        _menus[type] = s => menuBuilder((TSettings)s);
        _itemFactories[type] = () => createItem();
        _defaultSizes[type] = defaultSize;

        PlotTypes.Add(new PlotTypeOption(
            Label: label,
            AddCommand: new RelayCommand(() => StartPlacement(createSettings))));
    }

    private IReadOnlyList<ContextMenuProvider> GetMenuFor(PlotSettings settings)
        => _menus.TryGetValue(settings.Type, out var b) ? b(settings) : Array.Empty<ContextMenuProvider>();

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
            Select(null);
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

    // ---- Selection (single-select; bumps z-index, toggles thumbs) ----

    private void Select(PlotItem? item)
    {
        if (ReferenceEquals(_selected, item)) return;

        _selected?.Thumbs?.Hide();
        _selected = item;

        if (item is not null)
        {
            item.Thumbs?.Show();
            if (item.Container is not null)
                Panel.SetZIndex(item.Container.Outer, _nextZIndex++);
        }
    }

    // ---- Plot lifecycle ----

    private void AddPlotAt(PlotSettings settings, Point worksheetPoint)
    {
        if (_canvas is null) return;
        if (!_itemFactories.TryGetValue(settings.Type, out var itemFactory)) return;

        var item = itemFactory();
        item.DataContext = settings;

        var size = _defaultSizes.TryGetValue(settings.Type, out var ds) ? ds : new Size(400, 200);
        item.Width = size.Width;
        item.Height = size.Height;

        var snap = _snapSize;
        var snappedX = Snap(worksheetPoint.X, snap);
        var snappedY = Snap(worksheetPoint.Y, snap);

        // Build the per-plot visual tree:
        //   outer (Canvas, positioned on worksheet)
        //     └─ host (Grid)
        //          ├─ plot item    (z=0, axes/labels via ScottPlot)
        //          ├─ dynamic surf (z=1, WriteableBitmap data layer)
        //          └─ drag layer   (z=2, transparent pointer capture)
        var outer = new Canvas { Width = size.Width, Height = size.Height };
        var host  = new Grid   { Width = size.Width, Height = size.Height };

        host.Children.Add(item);
        Panel.SetZIndex(item, 0);

        var dynamicSurface = new DynamicBitmap();
        host.Children.Add(dynamicSurface);
        Panel.SetZIndex(dynamicSurface, 1);

        var dragLayer = new Border
        {
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeAll
        };
        host.Children.Add(dragLayer);
        Panel.SetZIndex(dragLayer, 2);

        outer.Children.Add(host);
        Canvas.SetLeft(outer, snappedX);
        Canvas.SetTop(outer,  snappedY);

        var container = new PlotContainer(outer, host, dragLayer, dynamicSurface);
        item.Container = container;

        item.AttachContextMenu(() => GetMenuFor(item.Settings));

        var thumbs = ThumbManager.Wire(container, item, () => _snapSize);
        item.Thumbs = thumbs;

        _plots[settings.PlotId] = item;

        DragHandler.Wire(item, _canvas, Select, () => _snapSize);

        _canvas.Children.Add(container.Outer);
        Select(item);

        // Cache the latest data-area rect on the item (DragHandler reads it
        // for snap math) and re-position/resize the dynamic bitmap surface so
        // it overlays the WpfPlot's data rect exactly.
        item.DataAreaChanged += rect =>
        {
            item.DataArea = rect;
            dynamicSurface.Sync(rect);
        };

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
                item.DataAreaChanged -= handler;
                return;
            }
            AlignToGrid(container, rect, snappedX, snappedY, snap);
        };
        item.DataAreaChanged += handler;

        _session?.AddPlot(item);
    }

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

        if (ReferenceEquals(_selected, item))
            _selected = null;

        if (item.Container is not null)
            _canvas?.Children.Remove(item.Container.Outer);
        _plots.Remove(plotId);
        _session?.RemovePlot(plotId);
    }

    // ---- Session binding ----

    public void BindSession(ViewportSession session)
    {
        _session = session;
        foreach (var item in _plots.Values)
            session.AddPlot(item);
    }

    public void UnbindSession() => _session = null;

    private static double Snap(double v, double s) => s > 0 ? Math.Round(v / s) * s : v;
}
