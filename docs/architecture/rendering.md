# Rendering

Each plot is composed of two layers stacked inside a `PlotItemHost`:

1. **Static chrome** — axes, tick labels, grid, plot title. Drawn by **ScottPlot 5** (`WpfPlot`). Updates only when `OnApplySettings` runs (axis range/scale/labels changed) or when the plot is resized.
2. **Dynamic data** — the actual histogram bars, oscilloscope trace, heatmap, etc. Drawn by **`DynamicBitmap`** (a `WriteableBitmap`-backed `Image`) sized + positioned to overlay the chrome's data rect.

This split is the core of the zero-lag design: the expensive layer (chrome) only redraws on rare user actions; the per-tick layer (data) is a pure memcpy from a worker-painted byte[] into a WriteableBitmap.

## Frame flow

```
ProcessingEngine (worker thread)
   ↓ paints into byte[pixelW * pixelH * 4]   ← Pbgra32, fresh per tick
   ↓ wraps in ProcessedData(buffer, w, h)
   ↓ DataStore.SetProcessed(plotId, data)

RenderingEngine (worker thread)
   ↓ ReferenceEquals(data, lastRendered) → if new, queue
   ↓ Coalesce into _pendingRenders dict (one slot per plot)
   ↓ Dispatcher.BeginInvoke(Render, RenderPendingOnUiThread)

PlotItem.Render (UI thread)
   ↓ data.IsEmpty ? DataLayer.Clear() : DataLayer.PresentBitmap(buf, w, h)

DynamicBitmap.PresentBitmap (UI thread)
   ↓ if size mismatch → new WriteableBitmap, set as Source
   ↓ _bitmap.WritePixels(rect, buffer, stride, 0)   ← memcpy into back buffer
   ↓ Visibility = Visible
```

## DynamicBitmap

[`DynamicBitmap.cs`](../../desktop/TelemetryPipeline/Telemetry.Viewer/Views/Plots/DynamicSurface/DynamicBitmap.cs):

- Subclasses `Image`. `Stretch = Fill`, `IsHitTestVisible = false` (clicks fall through to `DragLayer`), `BitmapScalingMode.NearestNeighbor`, `EdgeMode.Aliased`.
- `Sync(Rect dataArea)`: positions and sizes the surface to match the plot's data rect (in DIPs). Computes target pixel size from the current DPI and publishes via `Volatile.Write` so worker threads can read it.
- `TargetWidth` / `TargetHeight`: lock-free reads (volatile), used by the processor to size its byte[] correctly.
- `PresentBitmap(buffer, w, h)`: reuses the WriteableBitmap when dimensions match; otherwise reallocates. Does the WritePixels blit, sets Visibility back to Visible.
- `Clear()`: collapses Visibility so the underlying ScottPlot DataBackground (white) shows through. Used by the IsEmpty path and Clear Memory.

## Why a WriteableBitmap (not an `Image` source swap)?

`WritePixels` writes directly into the bitmap's existing back buffer — the WPF compositor doesn't see a new visual. That's why the histogram looks like it just _changed_ rather than getting torn down and rebuilt every tick. Source swaps would cause the layout system to re-measure and the compositor to re-rasterize.

## Why a fresh byte[] per tick?

We tried `ArrayPool<byte>.Shared` and got blink artifacts. The pool would return Plot A's painted buffer to the shared pool while the UI thread was still mid-`WritePixels`, then immediately rent it to Plot B's processor for overwrite. Allocating ~30-100 KB per tick per plot is cheap (the GC's gen 0 sweep handles it) compared to the cross-plot synchronization a pool would need to be safe.

## Why Dispatcher.BeginInvoke(Render)?

The default `DispatcherPriority` queue puts most user interactions higher than `Render`, so render dispatches don't preempt typing or button clicks. But `Render` is high enough that a queued blit fires before idle work — the result is that bitmap updates feel instantaneous without elbowing the user out of input handling.

We initially used a `SynchronizationContext.Post` (defaults to `Normal`) and got laggy plots. Switched to `Dispatcher.BeginInvoke(DispatcherPriority.Render, ...)` and the lag disappeared.

## Coalescing

`RenderingEngine._pendingRenders` is a `Dictionary<Guid, PendingRender>`. New entries overwrite old ones for the same plot — if the worker thread produces 3 frames for the same plot before the UI thread picks them up, only the latest gets rendered. We don't want a UI-bound queue that can grow unbounded if the UI ever lags.

`_renderPassScheduled` is an `Interlocked.Exchange`-gated flag — at most one `BeginInvoke` is queued at a time. After the dispatched action drains the pending dict, it re-checks for new arrivals and schedules itself again.

## Per-type rate gating

[`RenderingEngine.RenderingIntervals`](../../desktop/TelemetryPipeline/Telemetry.Viewer/Services/Pipeline/RenderingEngine.cs) sets a minimum interval per `PlotType`:

```csharp
[Oscilloscope]    = 33 ms      // ~30 Hz
[Histogram]       = 250 ms     // 4 Hz
[Pseudocolor]     = 250 ms     // 4 Hz
[SpectralRibbon]  = 250 ms     // 4 Hz
```

Same intervals on `ProcessingEngine.ProcessingIntervals` — there's no point computing more frames than we'll render. Oscilloscope wants live waveforms; analysis plots evolve slowly and 4 Hz is plenty for histogram refinement.

## Cross-thread DataContext access

A subtle one: `RenderTargetEntry.Type` is captured at registration time on the UI thread. The worker-thread `Tick` reading `target.Settings.Type` would throw because `Settings` is a `DependencyObject` with thread affinity. Caching `PlotType` in the entry sidesteps it entirely.

## ScottPlot

We use ScottPlot 5 in **WpfPlot** mode. We *don't* let ScottPlot render the per-tick data — we override its DataBackground to white, paint our own bitmap on top, and only call `Plot.Refresh()` when the chrome (axes/labels/limits) has to change. That happens inside `OnApplySettings` per type and on `Plot.Plot.RenderManager.RenderFinished` once we've learned the real DataRect.

`PlotItem.OnRenderFinished` broadcasts the data rect (in DIPs) so `PlotItemHost` can size `DynamicBitmap` to it. This event fires once per ScottPlot redraw, which is rare.

## Painting primitives

Processors use [`PixelCanvas`](../../desktop/TelemetryPipeline/Telemetry.Viewer/Services/Pipeline/Processors/PixelCanvas.cs) for clip-safe `Pixel`, `FillRect` and the canonical packed-color constants. Pbgra32 layout: B, G, R, A per pixel; `Pack(R,G,B,A)` masks them in.

For the pseudocolor heatmap, a 256-entry **Turbo** colormap LUT lives in `Colormaps.cs` (precomputed at static init from the canonical Turbo polynomial). One array index per pixel, no per-pixel float math.

## When to refresh ScottPlot chrome

Cheaply triggered:

- `OnApplySettings` runs whenever the bound `PlotSettings` raises `PropertyChanged` (range, scale, channel id, etc.). The base `PlotItem.ApplySettings` resets the plot's chrome (background colors, hidden grid), delegates to the subclass for axis/label setup, then calls `Refresh`.
- The histogram's `HistogramPlotItem.OnRender` checks `frame.YMax != _lastYMax` and calls `Plot.Plot.Axes.SetLimitsY(0, frame.YMax)` + `Refresh` only when the Y-axis ceiling actually changed. Otherwise it's a pure bitmap blit.

Cheap data-only path:

- All other ticks: `WritePixels` only. No `Refresh`, no measure/arrange.

## Failure modes

- **Tear**: never observed once we switched to per-frame `new byte[]`. WritePixels writes the whole buffer atomically.
- **Blink**: caused by sharing the buffer (pool / cached). Fixed.
- **Cross-thread crash on `target.Settings`**: caused by reading DependencyObject from worker. Fixed by caching `PlotType` at register time.
- **Stale frame after Disconnect**: `IsEmpty` path hides the bitmap, ScottPlot's white background shows through.
