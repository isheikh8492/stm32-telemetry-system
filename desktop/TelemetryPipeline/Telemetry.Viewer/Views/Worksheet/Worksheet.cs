using System.Collections.ObjectModel;
using System.Windows;
using Telemetry.Viewer.Common;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Models.Worksheet;
using Telemetry.Viewer.Services.ContextMenu;
using Telemetry.Viewer.Services.Pipeline;

namespace Telemetry.Viewer.Views.Worksheet;

// App-lifetime view-model for the worksheet. Holds:
//   * Plots          — observable collection bound to ItemsControl
//   * PlotTypes      — toolbar's Add-X buttons (one per registered type)
//   * Registry       — per-type catalog (factories, menus, default sizes)
//   * IsPlacing      — click-to-drop state machine (toolbar arms, canvas
//                      click drops; WorksheetGrid binds Cursor to this)
//   * _session       — current pipeline session, if any
//
// Worksheet builds NO visual tree. Each PlotPresenter renders via
// WorksheetGrid's ItemTemplate (a PlotItemHost), which hosts the per-type
// PlotItem and reports it back as an IRenderTarget via OnPlotItemReady.
public sealed class Worksheet : ObservableObject
{
    public ObservableCollection<PlotPresenter> Plots { get; } = new();
    public ObservableCollection<PlotTypeOption> PlotTypes { get; } = new();
    public PlotTypeRegistry Registry { get; } = new();

    public double SnapSize => 40;

    public System.Windows.Input.ICommand PopulateDefaultLayoutCommand { get; }

    private ViewportSession? _session;
    // PlotItem instances reported by hosted PlotItemHosts — needed so we can
    // (re)bind them to a session as it comes and goes. Keyed by presenter id.
    private readonly Dictionary<Guid, IRenderTarget> _renderTargets = new();

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

    public Worksheet()
    {
        PopulateDefaultLayoutCommand = new RelayCommand(PopulateDefaultLayout);
    }

    // ---- Placement (click-to-drop) ----

    private Func<PlotSettings>? _pendingFactory;
    public bool IsPlacing => _pendingFactory is not null;

    private void Arm(Func<PlotSettings> factory)
    {
        _pendingFactory = factory;
        OnPropertyChanged(nameof(IsPlacing));
    }

    private void Disarm()
    {
        if (_pendingFactory is null) return;
        _pendingFactory = null;
        OnPropertyChanged(nameof(IsPlacing));
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
        Registry.Register(type, defaultSize, createSettings, createItem, menuBuilder);

        PlotTypes.Add(new PlotTypeOption(
            Label: label,
            AddCommand: new RelayCommand(() => Arm(() => createSettings()))));
    }

    // ---- Click handling (called from WorksheetGrid code-behind) ----

    // Empty-canvas click: place if armed; otherwise clear selection.
    public void OnCanvasClick(Point at)
    {
        if (_pendingFactory is null)
        {
            Selected = null;
            return;
        }

        var settings = _pendingFactory();
        Disarm();

        var size = Registry.DefaultSize(settings.Type);
        var snap = SnapSize;
        var presenter = new PlotPresenter(
            settings,
            x:      Snap(at.X, snap),
            y:      Snap(at.Y, snap),
            width:  size.Width,
            height: size.Height,
            zIndex: _nextZIndex++);
        Plots.Add(presenter);
    }

    public void Select(PlotPresenter presenter) => Selected = presenter;

    // ---- Default layout ----

    // Drops a "show everything" layout — clears whatever is on the worksheet
    // first, then populates:
    //   1. 4 spectral ribbons (full width, one per param)
    //   2. 60 × 4 histograms (one per channel × param)
    //   3. 16 pseudocolors (PeakHeight × Area, one per channel for ch0..ch15)
    public void PopulateDefaultLayout()
    {
        Plots.Clear();
        Selected = null;

        var paramTypes = new[] { ParamType.Area, ParamType.PeakHeight, ParamType.PeakWidth, ParamType.Baseline };
        var channelIds = SelectionStrategy.AllChannelIds();
        var channelCount = channelIds.Count;

        const double margin  = 12;
        const double startX  = 40;
        const double startY  = 40;
        const double histW   = 160;
        const double histH   = 120;
        const double pcSize  = 240;

        // 4 spectral ribbons stacked at the top, each at its registered
        // default size — don't stretch to grid width.
        var ribbonSize = Registry.DefaultSize(PlotType.SpectralRibbon);
        var y = startY;
        foreach (var p in paramTypes)
        {
            var s = new SpectralRibbonSettings(
                plotId:     Guid.NewGuid(),
                channelIds: channelIds,
                param:      p,
                binCount:   BinCount.Bins128,
                minRange:   1, maxRange: 1_000_000,
                scale:      AxisScale.Logarithmic);
            Plots.Add(new PlotPresenter(s, x: startX, y: y, width: ribbonSize.Width, height: ribbonSize.Height, zIndex: _nextZIndex++));
            y += ribbonSize.Height + margin;
        }
        y += margin;

        // Histogram grid — 7 columns, channels flow left-to-right then wrap.
        // Each param fills its own contiguous block of rows so the layout
        // reads as 4 stacked param-sections of (channels in 7 cols × ~9 rows).
        const int cols = 7;
        var rowsPerParam = (channelCount + cols - 1) / cols;
        for (int pi = 0; pi < paramTypes.Length; pi++)
        {
            for (int c = 0; c < channelCount; c++)
            {
                var s = new HistogramSettings(
                    plotId:    Guid.NewGuid(),
                    channelId: c,
                    param:     paramTypes[pi],
                    binCount:  BinCount.Bins64,
                    minRange:  1, maxRange: 1_000_000,
                    scale:     AxisScale.Logarithmic);
                var col = c % cols;
                var row = pi * rowsPerParam + c / cols;
                var x = startX + col * (histW + margin);
                Plots.Add(new PlotPresenter(s, x, y + row * (histH + margin), histW, histH, _nextZIndex++));
            }
        }
        y += paramTypes.Length * rowsPerParam * (histH + margin) + margin;

        // 16 pseudocolors — PeakHeight × Area per channel, ch0..ch15 in 4×4.
        for (int i = 0; i < 16 && i < channelCount; i++)
        {
            var s = new PseudocolorSettings(
                plotId:     Guid.NewGuid(),
                xChannelId: i, xParam: ParamType.PeakHeight,
                yChannelId: i, yParam: ParamType.Area,
                binCount:   BinCount.Bins128,
                xMinRange:  1, xMaxRange: 1_000_000,
                yMinRange:  1, yMaxRange: 1_000_000,
                xScale:     AxisScale.Logarithmic,
                yScale:     AxisScale.Logarithmic);
            int col = i % 4;
            int row = i / 4;
            var x = startX + col * (pcSize + margin);
            Plots.Add(new PlotPresenter(s, x, y + row * (pcSize + margin), pcSize, pcSize, _nextZIndex++));
        }
    }

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
        // PlotItemHost.Unloaded will fire OnPlotItemReleased for the session
        // teardown.
    }

    // ---- Session binding ----

    public void BindSession(ViewportSession session)
    {
        _session = session;
        foreach (var target in _renderTargets.Values)
            session.AddPlot(target);
    }

    public void UnbindSession() => _session = null;

    private static double Snap(double v, double s) => s > 0 ? Math.Round(v / s) * s : v;
}
