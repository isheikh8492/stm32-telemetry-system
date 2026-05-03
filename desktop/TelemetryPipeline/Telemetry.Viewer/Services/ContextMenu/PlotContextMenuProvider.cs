using Telemetry.Viewer.Models;

namespace Telemetry.Viewer.Services.ContextMenu;

// Type-dispatched provider: callers register a builder per concrete PlotSettings type.
public sealed class PlotContextMenuProvider : IContextMenuProvider
{
    private readonly Dictionary<Type, Func<PlotSettings, IReadOnlyList<ContextMenuEntry>>> _builders = new();

    public void Register<T>(Func<T, IReadOnlyList<ContextMenuEntry>> builder)
        where T : PlotSettings
    {
        _builders[typeof(T)] = settings => builder((T)settings);
    }

    public IReadOnlyList<ContextMenuEntry> GetMenuFor(PlotSettings settings)
    {
        return _builders.TryGetValue(settings.GetType(), out var builder)
            ? builder(settings)
            : Array.Empty<ContextMenuEntry>();
    }
}
