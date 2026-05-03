using Telemetry.Engine;

namespace Telemetry.Viewer.Services.Pipeline;

// One connect/disconnect cycle of the data pipeline. Owns every object
// that has connection-scoped lifetime (reader, producer, consumer, buffer,
// viewport) and a deterministic Start/Stop/Dispose contract for the VM.
//
// Created by IPipelineFactory on Connect and disposed on Disconnect — it
// would be wrong to register these objects as DI singletons because their
// lifetime is shorter than the application's.
public interface IPipelineSession : IDisposable
{
    RingBuffer Buffer { get; }
    ViewportSession Viewport { get; }

    // Raised on the UI thread (the captured SynchronizationContext) when the
    // serial reader reports an error. VMs subscribe directly without needing
    // to marshal threads.
    event Action<string>? ErrorOccurred;

    void Start();
    void Stop();
}
