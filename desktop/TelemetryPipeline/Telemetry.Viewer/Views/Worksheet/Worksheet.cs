using System.Collections.ObjectModel;
using System.Windows.Input;
using Telemetry.Viewer.Common;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Worksheet;
using Telemetry.Viewer.Services.ContextMenu;
using Telemetry.Viewer.Services.Pipeline;
using Telemetry.Viewer.Views.Plots;

namespace Telemetry.Viewer.Views.Worksheet;

public sealed record PlotTypeOption(string Label, ICommand AddCommand);

// App-lifetime container for plots. Owns:
//   * Plots collection bound by the worksheet ItemsControl
//   * PlotTypes collection bound by the toolbar (one Add button per type)
//   * Per-plot-type context menu builders (used on right-click)
//   * The currently bound ViewportSession (set on Connect, cleared on Disconnect)
//
// Each <X>Plot static class registers its label, settings factory, and menu
// shape via RegisterPlotType — adding a new plot type touches Worksheet's
// ctor and one new <X>Plot file, nothing else.
public sealed class Worksheet
{
    public ObservableCollection<PlotSettings> Plots { get; } = new();
    public ObservableCollection<PlotTypeOption> PlotTypes { get; } = new();

    private readonly Dictionary<Guid, IPlotView> _loadedViews = new();
    private readonly Dictionary<Type, Func<PlotSettings, IReadOnlyList<ContextMenuProvider>>> _menus = new();

    private ViewportSession? _session;

    public Worksheet()
    {
        OscilloscopePlot.Register(this);
        // future: HistogramPlot.Register(this);
    }

    // ---- Plot-type registration (called from each <X>Plot.Register) ----

    public void RegisterPlotType<T>(
        string label,
        Func<T> createSettings,
        Func<T, IReadOnlyList<ContextMenuProvider>> menuBuilder) where T : PlotSettings
    {
        _menus[typeof(T)] = settings => menuBuilder((T)settings);
        PlotTypes.Add(new PlotTypeOption(
            Label: label,
            AddCommand: new RelayCommand(() => AddPlot(createSettings()))));
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
