using Telemetry.Viewer.Models;

namespace Telemetry.Viewer.Models.Worksheet;

// Anything that can live in the worksheet (plots, in the future possibly
// non-plot widgets like text panels, status indicators, etc.). Two members
// every worksheet item needs:
//   Id   — stable identity (for selection, persistence, lookup)
//   Name — display label (toolbar tabs, title bars, debug logs)
public interface IWorksheetItem
{
    Guid Id { get; }
    string Name { get; }
}

// A worksheet item that's a plot. Adds the PlotSettings the ProcessingEngine
// reads from. Settings is settable because the Properties dialog mutates it
// in place (the LivePlot raises PropertyChanged so bindings refresh).
//
// PlotId isn't on this interface — Id (from IWorksheetItem) returns the
// settings' PlotId for plots, so a separate property would just be redundant.
// PlotSettings records are immutable — to "edit" a plot, the VM replaces the
// record in its worksheet collection and the View's DataContext refreshes.
public interface IPlotView : IWorksheetItem
{
    PlotSettings Settings { get; }

    // Called once when the plot's UI control loads — sets up axes, labels,
    // anything that doesn't change per-frame.
    void InitializeStaticLayer();

    // Called each render tick with the latest processed data.
    void RenderDynamicLayer(ProcessedData data);
}
