namespace Telemetry.Engine;

public interface IContextMenuProvider
{
    IReadOnlyList<ContextMenuEntry> GetMenuFor(PlotSettings settings);
}

public sealed record ContextMenuEntry(string Label, Action OnInvoke);

// A render target that can host a context menu. The factory is invoked
// each time the menu opens so it always reflects the latest PlotSettings.
public interface IContextMenuTarget
{
    void AttachContextMenu(Func<IReadOnlyList<ContextMenuEntry>> entryFactory);
}
