# Plot types

Every plot type is wired into the app by **one file** under [`Telemetry.Viewer/Views/Plots/`](../../desktop/TelemetryPipeline/Telemetry.Viewer/Views/Plots) called `<Type>Plot.cs`. That file:

1. Registers the type's processor with `PlotProcessorRegistry`.
2. Calls `worksheet.RegisterPlotType<TSettings, TItem>(...)` with the toolbar label, default size, view factory, settings factory, and context-menu builder.

To add a new plot type, you create one of these wiring files plus 4 supporting files (Settings, Frame, Processor, PlotItem). No other files change.

## Type-by-type

### Oscilloscope

- **Source**: `OscilloscopePlot.cs`, `OscilloscopePlotItem.{xaml,cs}`, `OscilloscopePlotProcessor.cs`, `OscilloscopeSettings.cs`, `OscilloscopeFrame.cs`.
- **Data semantics**: most-recent event's raw 32 ADC samples per selected channel.
- **Processor**: reads `source.PeekLatest()`, paints one polyline per channel into a Pbgra32 buffer (Y axis fixed at 0..5000 ADC counts).
- **Rate**: full pipeline rate (20 ms processing, 33 ms render) — this is "live waveform" view.
- **Default**: 1 channel, registered size 280×160.

### Histogram

- **Source**: `HistogramPlot.cs`, `HistogramPlotItem.{xaml,cs}`, `HistogramPlotProcessor.cs`, `HistogramSettings.cs`, `HistogramFrame.cs`.
- **Data semantics**: trailing-window count distribution of one (channel, param) over the last `Capacity` events (10K).
- **Processor**: incremental — keeps `int[BinCount] Counts` + a parallel `int[Capacity] RingBins` FIFO. Per tick:
  1. If we fell behind (events evicted that we never processed), wipe and full-rebuild.
  2. For every event since `LastSequence`: bin it via `AxisFactory.For(Scale).GetBinIndex`, append to RingBins, increment `Counts[bin]`.
  3. If RingBins full: pop head, decrement `Counts[evicted]`.
  4. Find `maxCount` for Y-axis ceiling.
  5. Fresh `new byte[]` per tick — paint one solid black rectangle per non-empty bin.
- **Y axis**: starts at `FloorYMax = 100`, expands upward via `HistogramYAxisItem.NiceMax(maxCount)` to a 1/2/5×10ⁿ multiple so labels stay clean.
- **Rate**: 250 ms processing + render — 4 Hz is plenty for distribution evolution.
- **Default**: channel 0, PeakHeight, 256 bins, log scale, range 1..1,000,000.

### Pseudocolor (2D heatmap)

- **Source**: `PseudocolorPlot.cs`, `PseudocolorPlotItem.{xaml,cs}`, `PseudocolorPlotProcessor.cs`, `PseudocolorSettings.cs`, `PseudocolorFrame.cs`.
- **Data semantics**: 2D count heatmap of `(xChannel.xParam, yChannel.yParam)` — same trailing window.
- **Processor**: incremental, parallel `RingX[Capacity] / RingY[Capacity]` plus a 2D `Counts[bins, bins]`. Same 5-step pattern as Histogram but with two parallel arrays.
- **Colormap**: precomputed 256-entry **Turbo** LUT (`Colormaps.Turbo`). `int idx = (count * 255 / maxCount)` → look up Pbgra32.
- **Default**: x = ch0 PeakHeight, y = ch1 PeakHeight, 256 bins per axis, log/log, ranges 1..1,000,000.

### Spectral Ribbon

- **Source**: `SpectralRibbonPlot.cs`, `SpectralRibbonPlotItem.{xaml,cs}`, `SpectralRibbonPlotProcessor.cs`, `SpectralRibbonSettings.cs`, `SpectralRibbonFrame.cs`.
- **Data semantics**: one row per channel, each row a 1D histogram of (channel, param) over the trailing window — i.e. *N parallel histograms stacked vertically*. Reads as a heatmap where you can spot per-channel offsets at a glance.
- **Processor**: same incremental ring/bin pattern but 2D — `RingBins[capacity, channelCount]` + `Counts[channelCount, bins]`. Inner loop over the selection list.
- **Rate**: 250 ms.
- **Default**: all 60 channels, PeakHeight, 256 bins, log scale, full-width sized 1040×160.

## Common shape

Every processor implements [`IPlotProcessor`](../../desktop/TelemetryPipeline/Telemetry.Viewer/Services/Pipeline/Processors/IPlotProcessor.cs):

```csharp
ProcessedData? Process(PlotSettings settings, IDataSource source, int pixelWidth, int pixelHeight);
void ForgetState(Guid plotId) { }   // when a plot is removed
void ForgetAll()             { }   // when user clicks Clear Memory
```

Every view implements `PlotItem` which is `IRenderTarget`:

```csharp
void Render(ProcessedData data);   // called on UI thread
void Clear() { }                   // hide bitmap (Clear Memory path)
```

`PlotItem.Render` does the standard handshake:

```csharp
public void Render(ProcessedData data) {
    if (Host is null) return;
    OnRender(data);                                       // optional per-type tweaks
    if (data.IsEmpty) Host.DataLayerElement.Clear();      // hide bitmap → white DataBackground shows through
    else              Host.DataLayerElement.PresentBitmap(data.Buffer, data.PixelWidth, data.PixelHeight);
}
```

## Why incremental state matters

Without incremental state, every histogram with a 10K capacity buffer would do `O(10_000)` work per tick — 240 histograms × 4 ticks/s × 10K = ~10M binnings per second on the worker thread. With incremental state, per tick is `O(events arrived since last tick)` ≈ 10 events at 40 ev/s and 250 ms ticks. **~10000× less work** for the analysis plots, which is the single biggest reason the worksheet stays at zero lag.

## Pixel buffers

Every processor allocates a fresh `new byte[pixelWidth * pixelHeight * 4]` per tick. We tried pooling (`ArrayPool.Shared`) once — it caused histogram blink because the pool would hand the same buffer to a worker writing one plot while the UI thread was still mid-`WritePixels` on another. The cost of allocating ~30-100 KB per tick per plot is negligible compared to the synchronization overhead a shared pool would require.

## Pixel primitives

[`PixelCanvas.cs`](../../desktop/TelemetryPipeline/Telemetry.Viewer/Services/Pipeline/Processors/PixelCanvas.cs) is a tiny static helper for clip-safe `Pixel`, `FillRect`, and the canonical packed-color constants (`SteelBlue`, `Black`). Pbgra32 is premultiplied; helpers all assume opaque alpha.

## Adding a new plot type — checklist

1. `Models/Plots/<New>Settings.cs` — `PlotSettings` subclass with bindable fields.
2. `Models/Plots/<New>Frame.cs` — `ProcessedData` subclass for any per-frame metadata (e.g. histogram's `YMax`).
3. `Services/Pipeline/Processors/<New>PlotProcessor.cs` — implement `Process`, optional `ForgetState`/`ForgetAll`.
4. `Views/Plots/<New>PlotItem.{xaml,cs}` — `PlotItem` subclass with axes/labels via `OnApplySettings`.
5. `Views/Plots/<New>Plot.cs` — register processor + view at startup.
6. Add the new type to `Models/Plots/PlotType.cs` enum.
7. Wire into `MainWindowViewModel` ctor: `<New>Plot.Register(Worksheet, dialogs)`.

The toolbar's "Add X" button appears automatically because `PlotTypes` is observable.
