using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Telemetry.Viewer.Models.Worksheet;
using Telemetry.Viewer.Views.Plots.DynamicSurface;

namespace Telemetry.Viewer.Views.Worksheet;

// Composite per-plot view: hosts the type-specific PlotItem (axes/labels),
// the data-bitmap layer, the transparent drag overlay, and the corner resize
// thumbs. Created by ItemsControl from the worksheet's Plots collection;
// DataContext is a PlotPresenter.
//
// All per-plot wiring that used to live imperatively in Worksheet.AddPlotAt
// lives here:
//   * Resolves the right PlotItem via PlotTypeRegistry.CreateItem
//   * DragHandler — drag-to-move (updates Presenter.X/Y, snapped to grid)
//   * ThumbManager — corner resize (updates Presenter.W/H + X/Y, snapped)
//   * AttachContextMenu via the inner PlotItem
//   * Reports the PlotItem (as IRenderTarget) to the worksheet on Loaded so
//     it can be added to the active session
public partial class PlotItemHost : UserControl
{
    private PlotItem? _plotItem;
    private ThumbManager? _thumbs;
    private Worksheet? _worksheet;
    private PlotPresenter? _presenter;
    private Action<Rect>? _dataAreaListener;
    private Action<Rect>? _alignmentListener;
    // Click target (presenter.X/Y on first Loaded). After the first render
    // we adjust the host so the DATA RECT's TL — not the host's TL — lands
    // on the clicked grid intersection. Plot reflows axis labels on resize,
    // so this iterates a few rounds; if chrome is stable, later passes are
    // no-ops.
    private double _alignTargetX, _alignTargetY;
    private int _alignIters;

    internal Border DragLayerElement => DragLayer;
    internal DynamicBitmap DataLayerElement => DataLayer;
    internal Rect LastDataArea { get; private set; }

    public PlotItemHost()
    {
        InitializeComponent();
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    internal PlotItem? PlotItem => _plotItem;
    internal PlotPresenter? Presenter => _presenter;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_plotItem is not null) return;  // re-loaded; don't double-init
        if (DataContext is not PlotPresenter presenter) return;

        _presenter = presenter;
        _worksheet = FindAncestorDataContext<Worksheet>();
        if (_worksheet is null) return;

        _plotItem = _worksheet.Registry.CreateItem(presenter.Settings.Type);
        if (_plotItem is null) return;

        _plotItem.DataContext = presenter.Settings;
        _plotItem.Host = this;
        PlotHost.Content = _plotItem;

        // Sync data bitmap to the plot's data rect, and remember the rect for
        // drag/thumb snap math.
        _dataAreaListener = rect =>
        {
            LastDataArea = rect;
            DataLayer.Sync(rect);
        };
        _plotItem.DataAreaChanged += _dataAreaListener;

        _plotItem.AttachContextMenu(() => _worksheet.Registry.MenuFor(presenter.Settings));

        DragHandler.Wire(this, _worksheet);
        _thumbs = ThumbManager.Wire(this, _worksheet);
        _thumbs.SetVisible(presenter.IsSelected);

        presenter.PropertyChanged += OnPresenterChanged;

        _alignTargetX = presenter.X;
        _alignTargetY = presenter.Y;
        _alignmentListener = AlignToGrid;
        _plotItem.DataAreaChanged += _alignmentListener;

        _worksheet.OnPlotItemReady(presenter, _plotItem);
    }

    // Run for the first ~6 DataAreaChanged events: adjust the host so the
    // data rect's TL lands on the clicked grid intersection, and resize so
    // the data rect's W/H are integer multiples of snap. Plot reflows axis
    // labels on resize, so the rect changes again — iterate until chrome
    // stabilizes (later passes are no-ops). Then unsubscribe.
    private void AlignToGrid(Rect dataRect)
    {
        if (_presenter is null || _worksheet is null) return;
        if (++_alignIters > 6)
        {
            if (_alignmentListener is not null && _plotItem is not null)
                _plotItem.DataAreaChanged -= _alignmentListener;
            _alignmentListener = null;
            return;
        }

        var snap = _worksheet.SnapSize;
        // Snap disabled — skip alignment entirely. The plot stays at the
        // exact click point, sized to whatever defaults the registry gave it.
        if (snap <= 0)
        {
            if (_alignmentListener is not null && _plotItem is not null)
                _plotItem.DataAreaChanged -= _alignmentListener;
            _alignmentListener = null;
            return;
        }

        _presenter.X = _alignTargetX - dataRect.X;
        _presenter.Y = _alignTargetY - dataRect.Y;

        var leftChrome   = dataRect.X;
        var topChrome    = dataRect.Y;
        var rightChrome  = _presenter.Width  - dataRect.Right;
        var bottomChrome = _presenter.Height - dataRect.Bottom;

        var snappedDataW = Math.Max(snap, Math.Round(dataRect.Width  / snap) * snap);
        var snappedDataH = Math.Max(snap, Math.Round(dataRect.Height / snap) * snap);

        _presenter.Width  = snappedDataW + leftChrome + rightChrome;
        _presenter.Height = snappedDataH + topChrome  + bottomChrome;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_presenter is not null && _worksheet is not null)
            _worksheet.OnPlotItemReleased(_presenter);

        if (_plotItem is not null && _dataAreaListener is not null)
            _plotItem.DataAreaChanged -= _dataAreaListener;
        if (_plotItem is not null && _alignmentListener is not null)
            _plotItem.DataAreaChanged -= _alignmentListener;

        if (_presenter is not null)
            _presenter.PropertyChanged -= OnPresenterChanged;

        _plotItem = null;
        _thumbs = null;
        _presenter = null;
        _worksheet = null;
        _dataAreaListener = null;
        _alignmentListener = null;
        PlotHost.Content = null;
    }

    private void OnPresenterChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlotPresenter.IsSelected))
            _thumbs?.SetVisible(_presenter?.IsSelected == true);
    }

    // Walks up the logical/visual ancestry until it finds an element whose
    // DataContext is the requested type. Used to reach the Worksheet that
    // owns this host without coupling via static state or container refs.
    private T? FindAncestorDataContext<T>() where T : class
    {
        DependencyObject? obj = this;
        while (obj is not null)
        {
            if (obj is FrameworkElement fe && fe.DataContext is T match)
                return match;
            obj = VisualTreeHelper.GetParent(obj) ?? LogicalTreeHelper.GetParent(obj);
        }
        return null;
    }
}
