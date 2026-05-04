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

        _worksheet.OnPlotItemReady(presenter, _plotItem);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_presenter is not null && _worksheet is not null)
            _worksheet.OnPlotItemReleased(_presenter);

        if (_plotItem is not null && _dataAreaListener is not null)
            _plotItem.DataAreaChanged -= _dataAreaListener;

        if (_presenter is not null)
            _presenter.PropertyChanged -= OnPresenterChanged;

        _plotItem = null;
        _thumbs = null;
        _presenter = null;
        _worksheet = null;
        _dataAreaListener = null;
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
