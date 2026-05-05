# Worksheet Plot Pipeline: How It's Zero-Lag

A breakdown of how the [Worksheet repo](https://github.com/isheikh8492/Worksheet) keeps 100s of live plots responsive, and the patterns we'd port to match it.

## High-level data flow

```
SerialReader / source
        │
        ▼
IChannelDataBuffer        ← pre-extracts feature columns at append time
        │  (per-feature double[] ring + sequence cursor)
        ▼
PlotProcessor.Process     ← worker thread, incremental
        │  (one State<T> per plot in a Dictionary<Guid, ...>)
        ▼
DataStore                 ← keyed by plotId; latest ProcessedPlotData
        │
        ▼
RenderingEngine.Tick      ← polls store, queues PendingRender per plot
        │
        ▼  (Dispatcher.BeginInvoke at DispatcherPriority.Render)
        │
PlotView.Render           ← UI thread
        │  ├── ApplyConfigIfChanged → ExecuteStaticRefresh (rare)
        │  ├── UpdateHistogramScale → static refresh only on snap-bound change
        │  └── PresentBitmap or Clear
        ▼
DynamicBitmap (Image)     ← WriteableBitmap blit
```

Each arrow is one direction, one thread, no shared mutable buffer crossing the boundary.

## What makes it zero-lag

### 1. Pre-extraction at append time

`IChannelDataBuffer` does **not** hold full `Event` objects. It exposes one ring per feature (PeakHeight, Area, etc.) of pre-extracted `double[]`. When an event arrives, the producer pulls each feature value once and appends to the relevant ring.

```csharp
// Conceptually:
ringByFeature[xFeature].Append(event.Channels[c].Parameters.PeakHeight);
ringByFeature[yFeature].Append(event.Channels[c].Parameters.Area);
```

A `Snapshot(featureIndex)` then returns `ChannelWindowSnapshot(double[] Values, ..., long StartSequence, long EndSequence)` — a `readonly record struct`, no allocation. Processors loop over `double[]` directly with no `switch` over `ParamType`, no per-event indirection through `Channel.Parameters`.

**Cost saved:** the per-event feature switch and channel list lookup we currently do inside `SelectionStrategy.TryExtract` happens **once at append**, not N times per tick across N plots.

### 2. Per-plot incremental state, not per-tick rebuilds

Each processor (`PlotProcessor` for histogram/pseudocolor/spectral ribbon, plus the gate processor) keeps a `Dictionary<Guid, FooState>` keyed by plot id. State holds:

- `Counts[]` (or `Counts[,]` for 2D) — running totals.
- `RingBins[]` — parallel ring sized to buffer capacity. `RingBins[i] = bin index that event-at-slot-i contributed to`.
- `RingStart`, `RingCount` — logical view of the ring.
- `LastProcessedSequence` — how far the state has consumed.
- `PixelBuffer` — same byte[] every tick.
- All static-layer settings copied (BinCount, FeatureIndex, ScaleType, Min, Max, Capacity) so a `Matches(...)` predicate can detect change without versioning.

Each tick the processor:
1. `NeedsRebuild(state.LastProcessedSequence, snapshot)` — true if we fell behind further than the buffer holds. Triggers a full rewalk.
2. `ApplyXxxRange(state, snapshot, fromSeq, snapshot.EndSequence, ...)` — walks **only** sequences in `[fromSeq, EndSequence)`.
3. Each new event: bin it, `AppendXxxContribution(state, bin)` does
   - if ring full → evict head (`Counts[RingBins[RingStart]]--`)
   - append to tail (`RingBins[(RingStart+RingCount) % cap] = bin`)
4. `TrimXxxToWindow(state, snapshot.Count)` — for windows smaller than capacity.

Per-tick work is **O(events arrived since last tick)**, not O(snapshot). At 41 ev/s × 50 Hz tick rate that's ~1 event/tick instead of 10,000.

### 3. Settings-change detection without versioning

Instead of a monotonic `Version` counter, each state stores the static-layer-affecting settings (BinCount, FeatureIndex, AxisScaleType, MinValue, MaxValue, Capacity) and exposes `Matches(...)`:

```csharp
if (!state.Matches(bins, settings.XFeature, settings.XAxisScaleType, ...))
    state.Reset(...);
```

Mismatch → wipe Counts, RingBins, LastProcessedSequence; next tick rebuilds. Uses each field exactly as a key — no false invalidations from unrelated property changes.

### 4. Cached pixel buffer per plot

`state.PixelBuffer` is allocated **once per (plot, pixel size)** and reused. Worker thread overwrites in place each tick. The frame returned to the renderer (`HeatmapProcessedData(... PixelBuffer ...)`) holds the same byte[] reference.

WPF's `WriteableBitmap.WritePixels` copies synchronously from the source byte[] into its internal `BackBuffer` — by the time the call returns, the source can be reused. Worksheet relies on this to share the buffer across worker writes and UI reads without locks. (It's the contract per WPF docs; in practice we observed a flicker race with shared `ArrayPool` because pool buffers travelled between unrelated plots, but per-plot owned buffers don't have that issue.)

**LOH allocation rate:** zero per tick. Big win for pseudocolor (230KB) and spectral ribbon (665KB+) where per-frame `new byte[]` would shovel hundreds of MB/sec into LOH and trigger Gen2 collections every few seconds.

### 5. Color → pixel via precomputed palette LUT

```csharp
private static readonly byte[] PseudocolorPalette = BuildPseudocolorPalette();
// 256 × 4 bytes = a flat lookup table built once at startup.

state.PixelBuffer[i + 0] = PseudocolorPalette[paletteOffset + 0];
state.PixelBuffer[i + 1] = PseudocolorPalette[paletteOffset + 1];
state.PixelBuffer[i + 2] = PseudocolorPalette[paletteOffset + 2];
state.PixelBuffer[i + 3] = PseudocolorPalette[paletteOffset + 3];
```

One array index instead of our per-cell piecewise-linear Turbo interpolation. For a 128×128 heatmap that's 16K saved interpolations per tick. With 16 pseudocolors × 50 Hz, ~13M saved interpolations/sec.

### 6. ScottPlot static-layer refresh gating

Plot.Refresh() is **expensive** — ScottPlot redraws axes, labels, gridlines, and re-renders the data background. Worksheet only calls it when something visible to that layer actually changed:

```csharp
public override void Render(WpfPlot plot, ProcessedPlotData data) {
    bool staticChanged    = ApplyConfigIfChanged(plot);   // BinCount, scale, range, label, ...
    bool yTickLabelsChanged = UpdateHistogramScale(...);  // snap-bound only — not raw maxCount
    if (staticChanged || yTickLabelsChanged)
        ExecuteStaticRefresh(plot, () => _yAxisItem.Apply(plot, _yAxisUpperBound));

    RenderHistogramDynamic(histogram);  // always — but no Refresh
}
```

`PlotConfigSnapshot` is a `record struct` of static-affecting fields; equality is a cheap field-by-field compare. `UpdateHistogramScale` returns true only when the **NiceMax-snapped** upper bound changed, not on every count delta. Result: in steady state, zero Refresh calls per tick — only `WritePixels`.

### 7. `Surface.Clear()` for empty data

When the snapshot holds no events for a plot's selected channel/feature:

```csharp
if (heatmapData.IsEmpty) {
    surface.Clear();   // Visibility = Collapsed, _bitmap = null
    return;
}
```

Empty bitmap is **hidden**, not blitted as a transparent buffer. Avoids brief "flash of nothing" frames where the prior data flickers through during settings transitions or quiet periods.

### 8. Render-pass dispatch at `DispatcherPriority.Render`

```csharp
_dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(RenderPendingOnUiThread));
```

Higher than `Normal`. Plot updates compete with input/layout passes only at the WPF render-tier priority, so they don't queue behind unrelated app work — and don't preempt actual user input either (input is `DispatcherPriority.Input`, lower).

### 9. Snapshot is a `readonly record struct`

`ChannelWindowSnapshot` and `MultiChannelWindowSnapshot` are value types. Snapshots are **not** allocations. Multiple processors can take snapshots per tick without GC churn:

```csharp
public readonly record struct ChannelWindowSnapshot(
    double[] Values,         // ref to the buffer's existing array
    int StartIndex,
    int Count,
    int Capacity,
    long Version,
    long StartSequence,
    long EndSequence);
```

The `Values` array is the buffer's *internal* array — not a copy. The struct exposes `StartIndex`/`Count` so the caller indexes into it correctly. Reading is a pointer-arithmetic loop:

```csharp
for (int i = 0; i < snapshot.Count; i++)
    var value = snapshot.Values[(snapshot.StartIndex + i) % snapshot.Capacity];
```

Compare to our `Snapshot()` which `new[]`s a fresh `Event[]` of `_count` events every tick.

### 10. Multi-feature snapshots

For pseudocolor (X channel + Y channel) and spectral ribbon (multiple channels):

```csharp
MultiChannelWindowSnapshot snapshot = _buffer.GetSnapshot(settings.XFeature, settings.YFeature);
```

The buffer returns a struct that exposes both feature arrays at once, plus a shared sequence cursor. Processors walk in lockstep without re-extracting per dimension. Spectral ribbon over 60 channels asks for the 60-element snapshot and gets all rings; one event = one `(channelId, value)` pair per channel, all available without dictionary lookups.

## Summary table

| Lever | Saved cost (steady state) |
|---|---|
| Pre-extracted feature rings | Per-event `switch` over ParamType, channel list lookup |
| Incremental binning + RingBins | O(snapshot) bin computes per tick → O(N_new) |
| Per-plot cached `byte[] PixelBuffer` | LOH allocation churn → 0 |
| Palette LUT for colormap | N piecewise-linear interp per cell → 1 array index |
| `Refresh()` gated by `PlotConfigSnapshot` + snap-bound | Per-tick ScottPlot static redraw → ~0 |
| `Surface.Clear()` on empty | "Flash of nothing" frames → 0 |
| Render dispatch at `DispatcherPriority.Render` | Plot updates competing with low-priority work |
| `ChannelWindowSnapshot` as record struct | Per-tick `Event[] copy = new ...` allocation → 0 |
| Multi-feature snapshot in one call | Repeated source iteration / dictionary lookup |

## Mapping to our codebase

| Worksheet pattern | Our current state | Status |
|---|---|---|
| Pre-extracted feature rings | `RingBuffer<Event>` + per-tick `SelectionStrategy.TryExtract` | **gap** |
| Incremental processing state | Stateless processors that re-walk full snapshot | **gap** |
| Cached `byte[] PixelBuffer` | `new byte[...]` per Process call | **gap** |
| Turbo palette LUT | Per-cell piecewise-linear `Colormaps.Turbo` | **gap** |
| `Refresh()` gating | `Histogram` does it on every YMax change (no snap check) | **partial** |
| `Clear()` on empty | Always `PresentBitmap`, transparent if no data | **gap** |
| Render dispatcher priority | `SynchronizationContext.Post` (Normal) | **gap** |
| Snapshot as struct | `IReadOnlyList<Event>` allocated per tick | **gap** |
| Multi-feature snapshot | Single `Snapshot()` of full Events; double extraction in processor | **gap** |

The biggest single win is **(1) + (2) together** — pre-extracted feature rings *enable* incremental processing because the buffer has stable per-feature ring positions and the processor only ever sees `double` values. Doing incremental processing without pre-extraction (what I attempted) means the processor still pays the per-event `TryExtract` cost on every new event, which is small per-event but becomes the next bottleneck once binning is no longer the cost.
