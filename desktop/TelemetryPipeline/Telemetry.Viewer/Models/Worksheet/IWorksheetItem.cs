using Telemetry.Viewer.Models;
using Telemetry.Viewer.Services.ContextMenu;

namespace Telemetry.Viewer.Models.Worksheet;

// Anything that can live in the worksheet (plots, in the future possibly
// non-plot widgets like text panels, status indicators, etc.).
public interface IWorksheetItem
{
    Guid Id { get; }
    string Name { get; }
}

// A worksheet item that's a plot. Owns its PlotSettings (mutable INPC, the
// pipeline picks up changes via Version), knows how to render itself in two
// layers, and exposes a context-menu hook the worksheet/session can attach to.
public interface IPlotView : IWorksheetItem
{
    PlotSettings Settings { get; }

    // Called once when the plot's UI control loads — sets up axes, labels,
    // anything that doesn't change per-frame.
    void InitializeStaticLayer();

    // Called each render tick with the latest processed data.
    void RenderDynamicLayer(ProcessedData data);

    // Factory is invoked each time the menu opens so it always reflects the
    // latest PlotSettings.
    void AttachContextMenu(Func<IReadOnlyList<ContextMenuEntry>> entryFactory);
}
