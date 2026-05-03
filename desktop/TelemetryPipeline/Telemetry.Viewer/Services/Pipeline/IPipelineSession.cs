using Telemetry.Engine;
using Telemetry.IO;

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
    SerialReader Reader { get; }
    RingBuffer Buffer { get; }
    ViewportSession Viewport { get; }

    void Start();
    void Stop();
}
