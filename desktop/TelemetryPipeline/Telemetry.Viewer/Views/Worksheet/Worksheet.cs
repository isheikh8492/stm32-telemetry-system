using System.Collections.ObjectModel;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Worksheet;
using Telemetry.Viewer.Services.ContextMenu;
using Telemetry.Viewer.Services.Pipeline;
using Telemetry.Viewer.Views.Plots;

namespace Telemetry.Viewer.Views.Worksheet;

// App-lifetime container for plots. Survives connect/disconnect cycles —
// the active ViewportSession is bound on connect and unbound on disconnect,
// but the plot list and any loaded views persist.
//
// Owns the per-plot-type context-menu wiring: each <X>Plot static class
// registers its menu shape + dialog routing in the ctor below. Pipeline
// session is purely about data flow and never sees menu plumbing.
//
// (Future home for grid layout / drag-and-drop / tab grouping.)
public sealed class Worksheet
{
    public ObservableCollection<PlotSettings> Plots { get; } = new();

    private readonly Dictionary<Guid, IPlotView> _loadedViews = new();
    private readonly Dictionary<Type, Func<PlotSettings, IReadOnlyList<ContextMenuProvider>>> _menus = new();

    private ViewportSession? _session;

    public Worksheet()
    {
        // Plot types known to this app self-wire here. Adding a new plot
        // type = one extra <X>Plot.Register(this) call.
        OscilloscopePlot.Register(this);
        // future: HistogramPlot.Register(this);
    }

    // ---- Plot-type registration (called from each <X>Plot.Register) ----

    public void RegisterMenu<T>(Func<T, IReadOnlyList<ContextMenuProvider>> builder)
        where T : PlotSettings
    {
        _menus[typeof(T)] = settings => builder((T)settings);
    }

    private IReadOnlyList<ContextMenuProvider> GetMenuFor(PlotSettings settings)
    {
        return _menus.TryGetValue(settings.GetType(), out var builder)
            ? builder(settings)
            : Array.Empty<ContextMenuProvider>();
    }

    // ---- Plot lifecycle ----

    public void AddPlot(PlotSettings settings)
    {
        Plots.Add(settings);
    }

    public void RemovePlot(Guid plotId)
    {
        for (int i = 0; i < Plots.Count; i++)
        {
            if (Plots[i].PlotId == plotId)
            {
                Plots.RemoveAt(i);
                break;
            }
        }
        _loadedViews.Remove(plotId);
        _session?.RemovePlot(plotId);
    }

    // The plot UserControl calls this on Loaded. Worksheet attaches the
    // per-type context menu here (settings is mutable so the closure always
    // sees current state) and forwards to the active session, if any.
    public void NotifyViewLoaded(IPlotView view)
    {
        _loadedViews[view.Id] = view;

        view.AttachContextMenu(() => GetMenuFor(view.Settings));

        _session?.AddPlot(view);
    }

    // ---- Session binding ----

    public void BindSession(ViewportSession session)
    {
        _session = session;
        foreach (var view in _loadedViews.Values)
            session.AddPlot(view);
    }

    public void UnbindSession()
    {
        _session = null;
    }
}
