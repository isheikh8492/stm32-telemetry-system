namespace Telemetry.Viewer.Models;

// Base for everything PlotProcessor emits. Carries the pre-painted Pbgra32
// pixel buffer alongside whatever shape-specific state a subclass adds —
// PlotItem.Render is then just a memcpy onto DynamicBitmap.
//
// Two concrete-shape branches:
//   EventFrame    — per-event visualization (Oscilloscope, scatter-of-one-event, ...)
//   AnalysisFrame — across-events accumulation (Histogram, Pseudocolor, SpectralRibbon, ...)
public abstract record ProcessedData(byte[] Buffer, int PixelWidth, int PixelHeight)
{
    // True when the frame represents "no data to show" — PlotItem.Render
    // hides the bitmap (Surface.Clear) instead of presenting an empty buffer.
    // Set via object-initializer at construction: `new HistogramFrame(...) { IsEmpty = true }`.
    public bool IsEmpty { get; init; }
}
