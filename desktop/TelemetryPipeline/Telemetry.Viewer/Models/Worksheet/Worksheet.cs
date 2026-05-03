using System.Collections.ObjectModel;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Services.Pipeline;

namespace Telemetry.Viewer.Models.Worksheet;

// App-lifetime container for plots. Survives connect/disconnect cycles —
// the active ViewportSession is bound on connect and unbound on disconnect,
// but the plot list and any loaded views persist.
//
// (Future home for layout / drag-and-drop / tab grouping.)
public sealed class Worksheet
{
    public ObservableCollection<PlotSettings> Plots { get; } = new();

    // PlotId -> live view. Populated by views via NotifyViewLoaded; iterated
    // on session bind so every existing view registers with the new pipeline.
    private readonly Dictionary<Guid, IPlotView> _loadedViews = new();

    private ViewportSession? _session;

    public void AddPlot(PlotSettings settings)
    {
        Plots.Add(settings);
        // No view exists yet — it'll register itself when the DataTemplate
        // instantiates the UserControl and Loaded fires.
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

    // The plot UserControl calls this on Loaded. We stash it for future
    // session binds, and register immediately if a session is already active.
    public void NotifyViewLoaded(IPlotView view)
    {
        _loadedViews[view.Id] = view;
        _session?.AddPlot(view);
    }

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
