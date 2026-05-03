using System.Threading;
using Telemetry.Viewer.Services.ContextMenu;

namespace Telemetry.Viewer.Services.Pipeline;

// Factory between DI (singleton lifetime) and the pipeline session (per-connection
// lifetime). The VM injects this factory and calls Create() each time the user
// clicks Connect; the resulting session owns its own reader, producer, consumer,
// buffer, and viewport, all disposed on Disconnect.
public interface IPipelineFactory
{
    IPipelineSession Create(
        string portName,
        int baudRate,
        SynchronizationContext uiContext,
        IContextMenuProvider? contextMenuProvider = null);
}
