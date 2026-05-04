namespace Telemetry.Viewer.Models;

// Across-events analysis base. The frame represents the CURRENT state of
// an accumulator that absorbs events over time; EventsAccumulated advances
// monotonically as new events fold in. There is no single "event" this
// frame is "of" — it's a running snapshot.
public abstract record AnalysisFrame(long EventsAccumulated, byte[] Buffer, int PixelWidth, int PixelHeight)
    : ProcessedData(Buffer, PixelWidth, PixelHeight);
