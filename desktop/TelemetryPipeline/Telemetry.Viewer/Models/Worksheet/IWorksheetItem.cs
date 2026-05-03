namespace Telemetry.Viewer.Models.Worksheet;

// Anything that can live on the worksheet — plots today, and in the future
// non-plot widgets (text panels, status indicators, group headers). The
// minimum every worksheet item needs:
//   Id   — stable identity (selection, persistence, lookup)
//   Name — display label (toolbar, title bar, debug logs)
public interface IWorksheetItem
{
    Guid Id { get; }
    string Name { get; }
}
