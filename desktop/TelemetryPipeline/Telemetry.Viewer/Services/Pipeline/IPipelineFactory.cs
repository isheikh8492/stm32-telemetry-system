namespace Telemetry.Viewer.Services.Pipeline;

// Factory between DI (singleton lifetime) and the pipeline session (per-connection
// lifetime). Captures the UI SynchronizationContext at construction so callers
// don't have to thread it through Create().
public interface IPipelineFactory
{
    IPipelineSession Create(string portName, int baudRate);
}
