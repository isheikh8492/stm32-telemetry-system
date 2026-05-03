namespace TelemetryViewer;

// Anything that can live in the worksheet (plots, in the future possibly
// non-plot widgets like text panels, status indicators, etc.).
public interface IWorksheetItem
{
    Guid Id { get; }
    string Name { get; }
}

// A worksheet item that renders a plot bound to PlotSettings.
public interface IPlotView : IWorksheetItem
{
    Guid PlotId { get; }
}
