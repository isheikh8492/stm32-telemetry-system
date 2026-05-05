# Data pipeline

End-to-end view: every event from the STM32 traverses 5 stages before pixels appear on screen. Each stage runs on its own thread or its own thread-pool, so backpressure in any one stage is bounded and doesn't stall the others.

```
┌────────────┐     ┌──────────────┐     ┌──────────────┐     ┌──────────────────┐     ┌──────────────────┐     ┌─────────┐
│ STM32      │ →   │ SerialReader │ →   │ SerialProducer│ →   │ BufferConsumer    │ →   │ ProcessingEngine  │ →   │ DataStore│
│ UART       │     │ thread       │     │ Channel<Event>│     │ Task               │     │ Timer (worker)    │     │ (locked) │
└────────────┘     └──────────────┘     └──────────────┘     └──────────────────┘     └──────────────────┘     └────┬────┘
                                                                       ↓                          ↓                  ↓
                                                            ChannelDataBuffer            ProcessedData (per-plot)    │
                                                            (ring of 10K events)         on shared store             │
                                                                                                                     ↓
                                                                                                          ┌──────────────────┐
                                                                                                          │ RenderingEngine  │
                                                                                                          │ Timer (worker)   │
                                                                                                          └────────┬─────────┘
                                                                                                                   ↓ Dispatcher.BeginInvoke(Render)
                                                                                                          ┌──────────────────┐
                                                                                                          │ PlotItem.Render  │
                                                                                                          │ → DynamicBitmap  │
                                                                                                          └──────────────────┘
```

## Stage 1 — `SerialReader` (Telemetry.IO)

Owns the `SerialPort`. On its dedicated thread it loops:
1. Hunt for sync pair `0xA5 0x5A`.
2. Read 12-byte header → validate channel/sample counts.
3. Read sample block + params block.
4. Decode into `Telemetry.Core.Models.Event` (immutable record).
5. Fire `EventReceived` → `SerialProducer`.

A read timeout drops back to hunting; an `IOException` (port closed / unplugged) raises `ErrorOccurred` which is marshaled to the UI thread by `PipelineSession`.

## Stage 2 — `SerialProducer` (Telemetry.Engine)

A `System.Threading.Channels.Channel<Event>` sits between the reader thread and the consumer task. **Bounded at 1024**, `FullMode = DropOldest`. Single-reader, single-writer flags enable the lock-free fast path.

Why bounded with drop-oldest: if the consumer ever falls behind (rare — it just appends to the buffer), we'd rather drop the oldest queued frame than grow memory. The DataStore's trailing-window semantics make this invisible to plots.

## Stage 3 — `BufferConsumer` (Telemetry.Engine)

Single `await foreach` loop: `_buffer.Append(evt)` for each event off the channel. Runs on a dedicated `Task.Run` started by `PipelineSession`.

This is the seam where the DTO from `SerialReader` gets pre-extracted into the columnar buffer — the hot append path is the entire critical section for the producer side.

## Stage 4 — `ChannelDataBuffer` (Telemetry.Viewer.Services.DataSources)

The single source of truth for **what events are currently in memory**. Replaces the original `RingBuffer<Event>` with **per-(channel, param) feature rings**:

```
_featureRings: double[60 * 4][10_000]       // 240 rings × 10K events ≈ 19.2 MB
                                              // FeatureIndex = channelId * 4 + (int)ParamType
```

On `Append(Event)`:
- Default every feature slot to NaN (channels not in the event leave the slot empty).
- For each present channel: write its 4 params to the 4 corresponding feature rings.
- Bump `_writeIndex` (mod 10K) and `_totalAppended`.
- Save the latest event whole for the oscilloscope's raw-sample view.

On read (`GetSnapshot(featureIndex)` / `GetSnapshot(IReadOnlyList<int>, double[][])`):
- Returns a `ChannelWindowSnapshot` carrying the underlying `double[]` (no copy), `Count`, `Capacity`, and the absolute `(StartSequence, EndSequence)` range.
- Multi-feature variant fills a caller-allocated `double[][]` so plots over many channels (Spectral Ribbon over 60) don't allocate on every tick.

**Why this layout:** every analysis plot used to call `SelectionStrategy.TryExtract(evt, channelId, paramType)` once per event per tick — a per-event `Channel` list lookup + `ParamType` switch. Pre-extracting once at append time makes every read `O(events)` of pure double arithmetic.

`Clear()` zeroes every ring, resets `_writeIndex / _count / _totalAppended / _latestEvent`. Used by the **Clear Memory** button.

## Stage 5a — `ProcessingEngine` (Telemetry.Viewer.Services.Pipeline)

Worker-thread `Timer` (default 20 ms tick). Per tick:

1. Read `LatestEventId` — if `null`, no events yet, return.
2. Snapshot every plot's settings from `DataStore`.
3. **Per-plot rate gate**: skip if `nowTicks < _nextProcessAt[plotId]`. Per-type intervals:
   - Oscilloscope: 20 ms (full rate)
   - Histogram / Pseudocolor / Spectral Ribbon: 250 ms (4 Hz)
4. **Fingerprint check**: tuple of `(settings.Version, latestEventId, pixelW, pixelH)`. Skip if unchanged.
5. Look up the type's `IPlotProcessor`, call `Process(settings, source, pxW, pxH)` — returns `ProcessedData` containing the painted `byte[]` Pbgra32 buffer.
6. Store result via `DataStore.SetProcessed(plotId, data)`.
7. Schedule `_nextProcessAt[plotId] = nowTicks + interval`.
8. Detect stale plots (in fingerprints but no longer in settings) → broadcast `IPlotProcessor.ForgetState(plotId)` so per-plot incremental caches release.

The processors keep **per-plot incremental state** (running bin counts + a parallel `RingBins` FIFO) so per-tick cost is proportional to events arrived since last tick, not to snapshot size. See `docs/architecture/plot-types.md`.

## Stage 5b — `RenderingEngine` (Telemetry.Viewer.Services.Pipeline)

Worker-thread `Timer` (default 33 ms tick). Per tick:

1. Snapshot the registered render targets.
2. For each: if not yet due (per-type rate gate, same intervals as processing), skip.
3. Compare the latest `ProcessedData` to `LastRenderedData` by reference — if same, skip.
4. Stuff the `(entry, data)` into `_pendingRenders` map (one slot per plot — coalesces).
5. If anything new was queued: `Dispatcher.BeginInvoke(Render, RenderPendingOnUiThread)`.

`RenderPendingOnUiThread` runs on the UI thread:
- Drain the pending map under lock.
- For each: `entry.Target.Render(data)` — the target is a `PlotItem` which delegates to `DynamicBitmap.PresentBitmap` (a `WriteableBitmap.WritePixels` call — pure memcpy).
- Record `LastRenderedData` so the next tick's reference check works.

Critical detail: `RenderTargetEntry` caches `PlotType` at registration time (UI thread). Reading `target.Settings.Type` from the worker thread later would throw because `Settings` is a `DependencyObject` and has thread affinity.

## DataStore — the hub

`DataStore` is a single locked dictionary for `(plotId → settings)`, `(plotId → processed data)`, `(plotId → pixel size)`. Both engines + the `ViewportSession` (which manages plot lifecycle) talk to it. Lock contention is negligible because each operation is a few field reads/writes.

## ViewportSession — the lifecycle

`PipelineFactory.Create(port, baud)` produces a `PipelineSession` containing:
- The reader/producer/consumer/buffer chain
- A `ViewportSession` wrapping the engines + `DataStore`

Worksheet plots register through `ViewportSession.AddPlot(IRenderTarget)` (when `PlotItemHost.OnLoaded` fires) and unregister through `RemovePlot(plotId)` (on `OnUnloaded`).

`Clear Memory` cascades:
1. `ChannelDataBuffer.Clear()` — wipes feature rings, zeros `TotalAppended`.
2. `ProcessingEngine.ClearState()` — drops fingerprints + nextProcessAt + broadcasts `IPlotProcessor.ForgetAll()`.
3. `RenderingEngine.ClearAll()` — drops pending/last-rendered + dispatches `IRenderTarget.Clear()` (UI-thread bitmap hide).
4. `PipelineStatsViewModel.Reset()` — re-baselines the rate calc and zeros displayed counters.

The next event arriving from the firmware kicks off a fresh compute → render cycle for every plot.

## Threading summary

| Thread                     | Owner                   | Responsibility                                 |
|---|---|---|
| Serial-reader thread       | `SerialReader.Start`    | UART poll, frame decode                        |
| BufferConsumer task        | `Task.Run` from session | Drain `Channel<Event>` into `ChannelDataBuffer` |
| Processing-engine timer    | `System.Threading.Timer`| Per-plot compute, paint into byte[]            |
| Rendering-engine timer     | `System.Threading.Timer`| Coalesce → dispatch                             |
| WPF dispatcher             | UI thread                | `WritePixels` blits, plot lifecycle, input     |
