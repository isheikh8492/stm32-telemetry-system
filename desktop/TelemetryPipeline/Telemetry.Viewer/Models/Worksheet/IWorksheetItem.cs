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

    // Called by the rendering pipeline each time new processed data is ready
    // for this plot. Settings-driven scaffolding (axes, labels, ranges) is the
    // view's own concern — typically wired to Loaded + Settings.PropertyChanged.
    void Render(ProcessedData data);

    // Provider is invoked each time the menu opens so it always reflects the
    // latest PlotSettings.
    void AttachContextMenu(Func<IReadOnlyList<ContextMenuProvider>> contextMenuProvider);
}
