using Telemetry.Viewer.Models;

namespace Telemetry.Viewer.Services.ContextMenu;

public interface IContextMenuProvider
{
    IReadOnlyList<ContextMenuEntry> GetMenuFor(PlotSettings settings);
}

public sealed record ContextMenuEntry(string Label, Action OnInvoke);
