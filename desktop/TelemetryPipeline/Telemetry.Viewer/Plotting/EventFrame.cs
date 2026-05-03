namespace Telemetry.Viewer.Plotting;

// Per-event visualization base. The frame represents ONE specific event;
// EventId is meaningful — it identifies which event the data came from.
public abstract record EventFrame(uint EventId) : ProcessedData;
