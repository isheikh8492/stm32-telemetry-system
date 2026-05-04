using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using Telemetry.Viewer.Common;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Models.Worksheet;
using Telemetry.Viewer.Services.ContextMenu;
using Telemetry.Viewer.Services.Pipeline;

namespace Telemetry.Viewer.Views.Worksheet;

// App-lifetime view-model for the worksheet. Holds:
//   * Plots         — observable collection bound to ItemsControl
//   * Registry      — plot-type catalog
//   * Placement     — click-to-drop state machine
//   * _session      — current pipeline session, if any
// Worksheet does not build any visual tree. Each PlotPresenter renders via
// WorksheetGrid's ItemTemplate (a PlotItemHost), which hosts the per-type
// PlotItem and reports its IRenderTarget back via OnPlotItemReady.
public sealed class Worksheet : ObservableObject
{
    public ObservableCollection<PlotPresenter> Plots { get; } = new();
    public PlotTypeRegistry Registry { get; } = new();
    public PlotPlacementController Placement { get; }

    public ObservableCollection<PlotTypeOption> PlotTypes => Registry.PlotTypes;

    private ViewportSession? _session;
    // PlotItem instances reported by hosted PlotItemHosts — needed so we can
    // (re)bind them to a session as it comes and goes. Keyed by presenter id.
    private readonly Dictionary<Guid, IRenderTarget> _renderTargets = new();

    private double _snapSize = 40;
    private int _nextZIndex = 1;

    private PlotPresenter? _selected;
    public PlotPresenter? Selected
    {
        get => _selected;
        private set
        {
            if (ReferenceEquals(_selected, value)) return;
            if (_selected is not null) _selected.IsSelected = false;
            _selected = value;
            if (_selected is not null)
            {
                _selected.IsSelected = true;
                _selected.ZIndex = _nextZIndex++;
            }
            OnPropertyChanged();
        }
    }

    public double SnapSize => _snapSize;

    public Worksheet()
    {
        Placement = new PlotPlacementController(Registry, () => _snapSize);
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
        Registry.Register(
            type, label, defaultSize,
            createSettings, createItem, menuBuilder,
            onAddCommand: factory => Placement.Arm(factory));
    }

    // ---- Click handling (called from WorksheetGrid code-behind) ----

    // Empty-canvas click: place if armed; otherwise clear selection.
    public void OnCanvasClick(Point at)
    {
        if (Placement.IsArmed)
        {
            var presenter = Placement.TryPlace(at, _nextZIndex++);
            if (presenter is not null) Plots.Add(presenter);
        }
        else
        {
            Selected = null;
        }
    }

    public void Select(PlotPresenter presenter) => Selected = presenter;

    // ---- Plot lifecycle ----
    // PlotItemHost calls these when it has created/torn down its inner
    // PlotItem. Worksheet uses them to keep the session in sync.

    public void OnPlotItemReady(PlotPresenter presenter, IRenderTarget target)
    {
        _renderTargets[presenter.Id] = target;
        _session?.AddPlot(target);
    }

    public void OnPlotItemReleased(PlotPresenter presenter)
    {
        if (_renderTargets.Remove(presenter.Id))
            _session?.RemovePlot(presenter.Id);
    }

    public void RemovePlot(Guid plotId)
    {
        var presenter = Plots.FirstOrDefault(p => p.Id == plotId);
        if (presenter is null) return;
        if (ReferenceEquals(_selected, presenter)) Selected = null;
        Plots.Remove(presenter);
        // PlotItemHost.Unloaded will fire OnPlotItemReleased.
    }

    // ---- Session binding ----

    public void BindSession(ViewportSession session)
    {
        _session = session;
        foreach (var target in _renderTargets.Values)
            session.AddPlot(target);
    }

    public void UnbindSession() => _session = null;
}
