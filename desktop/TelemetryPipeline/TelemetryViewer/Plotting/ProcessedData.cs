namespace TelemetryViewer;

// Empty abstract root for everything the ProcessingEngine emits.
// Two concrete-shape branches inherit from it:
//   EventFrame    — per-event visualization (Oscilloscope, scatter-of-one-event, ...)
//   AnalysisFrame — across-events accumulation (Histogram, Pseudocolor, SpectralRibbon, ...)
public abstract record ProcessedData;
