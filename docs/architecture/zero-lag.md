# How zero-lag was achieved — every gap we closed

Zero-lag is a **system property**, not a single optimization. We had to get every layer right *at the same time* — fixing only the producer wouldn't have helped while the processors were O(snapshot); fixing only the processors wouldn't have helped while the renderer was dispatching at the wrong priority; and so on. This document is the exhaustive list of everything that had to be in place simultaneously.

Layer-by-layer: producer/consumer, buffer, processing engine, per-plot processors, rendering engine, UI blit path, ScottPlot integration, plot lifecycle, threading discipline. **Each section's items individually look minor; collectively they're the project.**

The single biggest realization was the buffer one — pre-extracting per-(channel, param) values at append time so plots read `double[]` slices instead of decoding events. But that fix alone would have done nothing if the rest of the stack was still wrong.

---

## Layer 1 — Producer / Consumer

The path from UART bytes to a typed `Event` available to processors. Wrong here = high baseline latency before anything downstream gets a chance.

| Gap | What we did | Why it matters |
|---|---|---|
| Text wire format capped throughput at ~10 ev/s. | Replaced with a fixed binary frame: 2-byte sync + LE u32/u16 fields + raw sample/param blocks. Decoded with `BinaryPrimitives.ReadXXLittleEndian` over `Span<byte>`. | The parser is no longer the bottleneck; UART becomes the rate limit (~40 ev/s at 2 Mbaud). |
| Reader thread was sharing the UI/dispatcher pool. | Dedicated thread via `Task.Run(_reader.Start)` with a synchronous read loop. | Predictable per-frame latency; no thread-pool starvation when the UI is busy. |
| No backpressure between reader and consumer. | `System.Threading.Channels.Channel<Event>` bounded at 1024 with `FullMode = DropOldest`. | If the consumer ever falls behind we drop the oldest queued frame instead of growing memory unbounded. The buffer's trailing-window semantics make a dropped frame invisible to plots. |
| Default channel uses lock-based reader/writer paths. | `SingleReader = true, SingleWriter = true` on the channel options. | Enables the lock-free fast path. We never have multiple writers (only `SerialReader`) or multiple readers (only `BufferConsumer`). |
| Stop/restart could replay stale events from the previous session. | `SerialProducer.Stop()` drains `_channel.Reader` after canceling. | Reconnect after disconnect starts cleanly — no zombie events from before the user stopped. |
| Reader errors crashed the whole pipeline. | `SerialReader.ErrorOccurred` event; `PipelineSession` marshals it to the UI thread via `_uiContext.Post`. | UI dialogs surface errors without ceremony; the rest of the pipeline can keep running through transient hiccups. |

## Layer 2 — Buffer (the big one)

The single most consequential refactor. Replacing `RingBuffer<Event>` with `ChannelDataBuffer` is the line that separates "naive 10K binnings per tick" from "10 binnings per tick".

| Gap | What we did | Why it matters |
|---|---|---|
| Plots were decoding the full `Event` per plot per tick. With 240 plots × 10K-event window × 4 ticks/sec, that's ~10M `SelectionStrategy.TryExtract` calls per second on the worker. | Built `ChannelDataBuffer`: **240 parallel `double[10_000]` rings**, one per `(channelId, ParamType)`. On `Append(Event)`, the producer pulls every (channel, param) value once and writes it into the right ring. Plots read `double[]` slices directly via `GetSnapshot(featureIndex)`. | Read path drops from "loop events, switch on ParamType, find channel by id" to "indexed read of a contiguous double array". Single biggest CPU win on the read side. |
| `evt.Channels.IndexOf(ch)` was being used as the channel id — but the firmware can send channels in any order or with gaps. | Index by `ch.ChannelId`, not list position. Skip if `ch.ChannelId >= channelCount`. | Without this, every histogram showed the wrong channel's distribution. The bug was silent — distributions still *looked* plausible until you compared them. |
| Channels missing from a frame would land in another channel's slot. | Default every feature slot to `double.NaN` at the start of each `Append`; only present channels overwrite. Processors check `double.IsNaN(v)` and skip. | Missing channels become visible non-data instead of corrupting an adjacent channel's distribution. |
| Multi-feature reads (Pseudocolor, Spectral Ribbon) were allocating `double[][]` per tick. | `GetSnapshot(IReadOnlyList<int>, double[][] outFeatures)` overload — caller pre-allocates the outer array, buffer fills it. | At 60 channels × 4 Hz this would otherwise be ~240 array allocations/sec just for spectral ribbon snapshots. |
| Concurrent reads while the producer was appending caused torn double writes. | Reads are explicitly *eventually consistent* — the buffer locks only around producer writes (full critical section per Append) and exposes the underlying `double[]` directly to readers. Per-cell deltas are below histogram bin granularity. | Lock-free read path keeps processor ticks fast. We documented the consistency model in code comments so it's an intentional choice, not an accident. |
| Oscilloscope still needs raw samples (the feature rings only carry derived params). | Preserved `PeekLatest()` returning the most recent whole `Event` alongside the feature rings. | Oscilloscope reads stay O(1); the feature-ring optimization didn't break the simplest plot type. |
| Clear-memory cascade left `_totalAppended` non-zero. | `Clear()` resets all ring slots, `_writeIndex`, `_count`, `_totalAppended`, `_latestEvent` together. | Stats panel shows 0 events post-clear; rate calc baseline is correct. |

## Layer 3 — Processing engine

The orchestrator deciding *which* plot needs work *when*. Skipping work is more important than doing it fast.

| Gap | What we did | Why it matters |
|---|---|---|
| Every plot recomputed every tick whether or not its inputs had changed. | Fingerprint tuple `(settings.Version, latestEventId, pixelW, pixelH)` cached per plot. Skip if unchanged. | Plots that haven't seen new events / settings changes / resizes consume zero CPU per tick. |
| Uniform tick rate wasted cycles on plots that don't need them. | Per-type rate gates: oscilloscope at **20 ms** processing / 33 ms render; histogram / pseudocolor / spectral ribbon at **250 ms**. Per-plot `_nextProcessAt` skip until each plot's interval elapses. | At 4 Hz vs 50 Hz, analysis plots do 12× less work for indistinguishable user-perceived quality. |
| Reentrant `Tick` calls during slow ticks would queue up indefinitely. | `Interlocked.Exchange(ref _isTicking, 1)` reentrant guard in `PollingEngine`. | If a tick takes longer than the interval, the next one is silently skipped instead of stacking up. |
| Removed plots leaked their per-processor state. | Detect stale plots (in `_fingerprints` but not in current settings) → broadcast `IPlotProcessor.ForgetState(plotId)` across all processors. | No memory creep over a long session as plots come and go. |
| `Pixel size = 0` (surface not yet sized) caused divide-by-zero or wasted work. | Skip plots where `pxW <= 0 || pxH <= 0`. | First-tick after a plot is added doesn't crash; processor activates once the surface has reported its real size. |
| Per-tick metrics weren't aggregated by type. | `RecordTime(PlotType, ms)` aggregates across all instances of a type. | Stats panel reads "Histogram avg 0.05 ms" not "240 separate histogram timings". Useful both for ops and for the BENCHMARKS doc. |

## Layer 4 — Per-plot processors (the algorithmic core)

The algorithmic shift from O(snapshot) to O(events arrived). 23.7× / 17.5× / 2.4× speedups all live here.

| Gap | What we did | Why it matters |
|---|---|---|
| Each tick rebuilt the bin counts from scratch by looping the entire snapshot. | **Incremental ring-FIFO**: per-plot `Counts[]` array + parallel `RingBins[]` FIFO sized to capacity. New events: bin → push → increment. Full FIFO: pop head → decrement evicted bin. | At steady state with 40 ev/s and 4 Hz ticks: ~10 events per tick instead of 10,000. **~1000× less algorithmic work** for histograms / pseudocolors / spectral ribbons. |
| Buffer wrap could leave the processor's state out of sync (events evicted that we never processed). | Detect via `state.LastSequence < snapshot.StartSequence` → call `state.WipeIncrementalIndex()` to clear and full-rebuild this tick. | Self-healing — if the processor falls behind, one full rebuild gets it back in sync without replicating stale state. |
| Settings changes (range, scale, channel id) didn't invalidate the cached state. | `state.SettingsVersion != hist.Version` → `state.Reset(hist.Version)`. | Editing a plot's properties updates immediately; old state doesn't bleed into the new view. |
| Buffer capacity / bin count changes leaked old state. | State key includes `(Capacity, BinCount/Bins/ChannelCount)` — mismatch triggers a fresh `new State(...)` allocation. | Resizing the buffer or changing bin counts is rare but no longer corrupts. |
| Pixel buffer pooling caused inter-plot contamination (the blink). | Reverted from `ArrayPool<byte>.Shared` to `new byte[pixelWidth * pixelHeight * 4]` per tick. | Allocations are gen-0 and free; cross-thread synchronization a shared pool would need is not. |
| Pseudocolor + Spectral Ribbon allocated `int[]` / `double[][]` for `featureIndices` and `outFeatures` per tick. | Pre-allocated in `State.FeatureIndices` / `State.FeatureBuf`, reused across ticks. | Eliminates per-tick allocation in the hottest loops. |
| Pseudocolor's per-pixel paint was doing float math (HSV → RGB). | Pre-computed **Turbo colormap LUT** at static init in `Colormaps.cs`. Per-pixel = single LUT lookup. | Heatmap paint is now memory-bound, not compute-bound. |
| Painting filled rectangles was bounds-checked per pixel. | `PixelCanvas.FillRect` clips once at the rect level; inner loop is unchecked. | Removes a branch from the inner loop; relies on the clip math being trustworthy (and unit-tested). |

## Layer 5 — Rendering engine

The bridge between worker-thread paint and UI-thread blit. Wrong dispatch priority alone could nuke 60 fps; wrong queueing nukes coalescing.

| Gap | What we did | Why it matters |
|---|---|---|
| `SynchronizationContext.Post` (default `Normal` priority) had render dispatches getting elbowed by user input. Plots felt sluggish even when frames were ready. | `Dispatcher.BeginInvoke(DispatcherPriority.Render, ...)`. | `Render` is high enough that queued blits fire ahead of idle work, but not so high they preempt typing or button clicks. The single fix that turned "sluggish" into "instant". |
| Worker thread reading `target.Settings.Type` to decide rate gating crashed (`DependencyObject` thread affinity). | Cache `PlotType` in `RenderTargetEntry` at registration time (UI thread). Worker reads the cache, never the DependencyObject. | App stays alive. WPF type affinity bites in non-obvious places. |
| Worker producing 3 frames for the same plot before the UI thread picked up any → UI rendered stale frames. | `_pendingRenders` is a `Dictionary<Guid, PendingRender>` — same-plot writes overwrite. | Only the latest frame ever renders. UI never gets behind. |
| Multiple in-flight UI dispatches could queue up. | Single in-flight flag via `Interlocked.Exchange(ref _renderPassScheduled, 1) == 1` short-circuit. After `RenderPendingOnUiThread` finishes, it re-checks pending and re-schedules itself if needed. | At most one `BeginInvoke` queued at a time; no UI-thread queue growth under load. |
| ProcessedData reference equality checks were giving false-positives. | `ProcessingEngine.SetProcessed` writes a new `ProcessedData` instance only when its fingerprint changes — `ReferenceEquals(data, entry.LastRenderedData)` becomes the correct "is this new?" test. | UI thread skips no-op renders cheaply; reference identity is faster than any value comparison. |
| Per-render metrics weren't recorded. | `RecordTime(entry.Type, elapsedMs)` inside the UI render loop. | Stats panel separates processing ms from rendering ms — diagnoses worker-bound vs UI-bound regressions. |
| Per-type render rate gating wasn't implemented (was uniform). | Added `RenderingIntervals` table (matches `ProcessingIntervals`). | Analytics renders 4× per sec, oscilloscope 30× per sec. UI thread does ~12× less work for indistinguishable quality. |

## Layer 6 — UI blit path (DynamicBitmap)

The actual paint. Sub-millisecond if it's a memcpy, multi-millisecond if it's a Source swap. We made sure it was always a memcpy.

| Gap | What we did | Why it matters |
|---|---|---|
| Source-swapping the `Image.Source` triggered a full layout pass + recomposition every tick. | `WriteableBitmap.WritePixels(rect, buffer, stride, 0)` — writes directly into the existing back buffer. | Per-blit cost drops to microseconds. Compositor sees "the same visual changed", not "a new visual to lay out". |
| Bitmap reallocation on every tick when sizes happened to match. | Reuse the WriteableBitmap when `_bitmap.PixelWidth == width && PixelHeight == height`. Reallocate only on size change. | Allocation only on resize, not per tick. |
| Worker threads needed to read pixel size to allocate the right buffer; locking was overkill. | `Volatile.Write` / `Volatile.Read` on `_targetWidth` / `_targetHeight`. | Lock-free snapshot consistency; workers always see the most recent UI-published size. |
| Stale frames flashed when a plot went empty (e.g. on Disconnect). | `IsEmpty` flag on `ProcessedData` → `DataLayer.Clear()` collapses Visibility so ScottPlot's white DataBackground shows through. | Empty plots render as empty, not as the last frame from before. |
| `BitmapScalingMode` defaulting to `Linear` caused per-pixel filtering during compositor scaling. | `RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor)` + `EdgeMode.Aliased`. | Compositor skips the smoothing pass — cheaper and matches our "the buffer is already at the correct pixel size" intent. |
| `IsHitTestVisible = true` was making the bitmap layer eat clicks meant for the drag overlay. | `IsHitTestVisible = false`. | Pointer events fall through to the transparent `DragLayer` which owns drag/select/menu. |

## Layer 7 — ScottPlot chrome integration

Letting ScottPlot do the (rare, expensive) chrome and keeping it out of the (frequent, hot) data path.

| Gap | What we did | Why it matters |
|---|---|---|
| `Plot.Refresh()` was being called every data tick — full ScottPlot redraw including axis layout. | `Refresh()` only on chrome changes (`OnApplySettings` for axis/range/labels/scale changes; `HistogramPlotItem.OnRender` only when `frame.YMax != _lastYMax`). | Per-tick UI cost drops from ~5-15 ms (full redraw) to <1 ms (just our memcpy). |
| Y-axis ceiling jittered with every histogram tick → `Refresh` every tick anyway. | `HistogramYAxisItem.NiceMax(maxCount)` rounds up to a 1/2/5×10ⁿ multiple — same ceiling for a range of counts → no spurious refreshes. | Histogram chrome stable across a wide range of bin-count values. |
| DataRect was being polled per tick — but ScottPlot only reports it after a render. | Subscribe to `Plot.Plot.RenderManager.RenderFinished`; broadcast `DataAreaChanged(rect)` on the dispatcher; `DynamicBitmap.Sync(rect)` resizes/positions the surface and re-publishes its target pixel size. | DataRect updates flow naturally, only when ScottPlot has actually rendered. |
| ScottPlot's transparent figure background bled the worksheet background through. | `Plot.FigureBackground.Color = Transparent`, `DataBackground.Color = White`. | Worksheet background visible around chrome; data area is the white canvas the bitmap blits onto (or shows through when `IsEmpty`). |

## Layer 8 — Plot lifecycle

Subtle but essential — what happens when a plot is added, resized, or removed determines whether the pipeline stays clean.

| Gap | What we did | Why it matters |
|---|---|---|
| `PlotItemHost.OnLoaded` was wiring everything in any order, leading to race conditions on first render. | Strict ordering: resolve `PlotItem` → set `DataContext` → wire `DataAreaChanged` (size sync) → attach context menu → wire `DragHandler` + `ThumbManager` → register with worksheet. | First tick after `OnLoaded` always has a sized surface and registered render target. |
| Click-to-place would drop the plot at the cursor — but axis chrome shifted the data rect on reflow, so the data wasn't centered where the user clicked. | `AlignToGrid` listener on `DataAreaChanged` — for the first ~6 layout passes, adjust `viewModel.X/Y/Width/Height` so the **data rect's TL** lands on the clicked grid intersection and W/H are integer multiples of snap. Self-unsubscribes once chrome stabilizes. | Plots placed on the grid actually align on the grid. Pre-fix, every plot was offset by ~30 px of axis chrome. |
| Removing a plot left its rendering target subscribed to the session. | `_teardown` action captured at `AddPlot` time, invoked at `RemovePlot` (or `Dispose`). | No ghost plots feeding the session after removal. |
| Pixel size was registered once at `AddPlot` — DPI changes / data-rect changes weren't republished. | Subscribe to `target.DataAreaChanged` in `ViewportSession.AddPlot`; republish on every change. | Resizing a plot cleanly updates the processor's target buffer size on the next fingerprint check. |

## Layer 9 — Threading discipline

The unifying constraint. Every cross-boundary communication had to be intentional.

| Gap | What we did | Why it matters |
|---|---|---|
| Reading mutable VM properties from worker threads (DependencyObjects, ObservableObjects). | Cache anything the worker needs at registration time on the UI thread (`PlotType`, `PixelType` etc.). Workers never touch DependencyObjects. | No thread-affinity crashes; clear ownership boundary. |
| Per-tick allocations on cross-thread boundaries. | Snapshot types (`ChannelWindowSnapshot`, `MultiChannelWindowSnapshot`) are `readonly record struct` — passed by value, no heap. | Gen-0 pressure stays low; no GC pauses visible at 30 Hz. |
| ScottPlot's `RenderFinished` fires on its own thread context. | `Plot.Dispatcher.Invoke(BroadcastDataArea)` to marshal back to the UI thread. | DataAreaChanged subscribers are guaranteed UI-thread context. |
| Events from `SerialReader` / `BufferConsumer` / engines could fire on any thread. | Errors marshaled via `_uiContext.Post`. UI subscribers (dialogs, command refresh) don't need their own dispatcher hops. | One cross-thread hop, in one place, predictable. |

## Layer 10 — The non-obvious "of course it has to be this way"

Things that look like style choices but were actually forced by performance constraints.

| Decision | What it actually buys us |
|---|---|
| **Pbgra32** as the pixel format | Premultiplied alpha; no per-pixel multiply during compositing. Native WPF format — no conversion on blit. |
| **Channel-major** sample layout in the wire format | Mirrors how the firmware composes the frame and how the host parses it. Single `Buffer.BlockCopy` per channel. |
| **uint** packed colors (not `Color` structs) | Stored once, written as 4 bytes per pixel. No struct field reads in the inner loop. |
| **Settings.Version** as a monotonic counter | Single integer compare invalidates fingerprints. No equality method, no allocation. |
| **`AxisFactory.For(scale)` returning a strategy** rather than `if (scale == Log) ... else ...` per-bin | One virtual call at the start of the tick, hot loop is monomorphic. |
| **Frozen** ChannelCatalog list shape | Reads from any thread without locking the list itself; only entries' Name/Color are observable. |
| **Bounded `Channel<Event>` with `DropOldest`** | Backpressure without growth; producer never blocks. |
| **Per-feature `double` (not `float`) rings** | One memory format throughout the pipeline; `int → double` cast at append, no conversion downstream. |

---

## The takeaway

If you're trying to reproduce zero-lag in a similar pipeline, here's the **non-negotiable top 8** — miss any one and the rest can't compensate:

1. **Pre-extract feature values at the producer side**, not at the consumer side. (Layer 2)
2. **Incremental processors with per-plot ring/bin FIFO state**, never naive recompute. (Layer 4)
3. **Per-plot-type rate gating** on both processing and rendering engines. (Layers 3 + 5)
4. **`Dispatcher.BeginInvoke(DispatcherPriority.Render)`** for UI dispatches — never `SynchronizationContext.Post` defaults. (Layer 5)
5. **Coalesced render queue** (one slot per plot, single in-flight dispatch flag). (Layer 5)
6. **`WriteableBitmap.WritePixels`** for the per-frame blit — never `Source` swaps. (Layer 6)
7. **ScottPlot.Refresh only on chrome changes**, not per data tick. (Layer 7)
8. **No DependencyObject reads from worker threads** — cache anything the worker needs at registration. (Layer 9)

Then the dozens of smaller gaps in the tables above. Zero-lag is what you get when **all** of them are right at the same time.
