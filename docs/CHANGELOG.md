# Changelog

This is a curated, milestone-grouped history. For full per-commit detail, run `git log`.

## v1.0 — Project complete

### Visualization polish
- Histograms render with **solid black fill** (was steel blue).
- Histogram Y-axis floor lowered from 1K → 100 → 10; expands upward via `NiceMax`.
- Default bin count for Histogram, Pseudocolor, Spectral Ribbon set to **256 bins**.
- **Default layout** drops 1 oscilloscope (full-width, channel 0) above 4 spectral ribbons, then 240 histograms (60 channels × 4 params, 7-column grid), then 16 pseudocolors (4×4, ch0..15).
- Show-grid checkbox starts **off** by default and properly toggles grid lines.
- Snap-to-grid checkbox starts **off** by default. Snap target is the data-rect edge (not the host edge) so plots line up with the chrome they display.

### Pipeline performance — "Zero-Lag Worksheet"
- Replaced raw `RingBuffer<Event>` with **`ChannelDataBuffer`** that pre-extracts every (channelId, ParamType) value at append time into 240 feature rings. Plots read `double[]` directly — no per-event `SelectionStrategy.TryExtract` / Channel-list lookup.
- All three analysis processors (Histogram, Pseudocolor, Spectral Ribbon) became **incremental**: per-plot `Counts` array + parallel `RingBins` FIFO. Per-tick work is O(events arrived since last tick) rather than O(snapshot). ~10000× less CPU at 41 ev/s with 260 plots.
- **Per-type rate gating** on both engines: oscilloscope at 20 ms processing / 33 ms render; histograms / pseudocolors / spectral ribbons at 250 ms.
- `RenderingEngine` switched from `SynchronizationContext.Post` (default Normal priority) to `Dispatcher.BeginInvoke(DispatcherPriority.Render)` for predictable UI-thread blits without elbowing input handling.
- `RenderTargetEntry` caches `PlotType` at register time (UI thread). Reading `target.Settings.Type` from the worker would throw — DependencyObject thread affinity.
- Coalesced render queue: `Dictionary<Guid, PendingRender>` with a single in-flight dispatch flag (`Interlocked.Exchange`).
- `PlotProcessor` keeps a fresh `new byte[]` per tick — pooling caused histogram blink due to buffer reuse mid-blit.

### Memory management
- **Clear Memory** button below Connect/Disconnect. Cascades:
  - `ChannelDataBuffer.Clear()` — wipes feature rings, zeros `TotalAppended`.
  - `ProcessingEngine.ClearState()` — drops fingerprints + nextProcessAt + broadcasts `IPlotProcessor.ForgetAll()`.
  - `RenderingEngine.ClearAll()` — drops pending/last-rendered + dispatches `IRenderTarget.Clear()` (UI-thread bitmap hide).
  - `PipelineStatsViewModel.Reset()` — re-baselines rate calc, zeros displayed counters, resets per-type metrics.

### Firmware (STM32F446ZE)
- Switched amp jitter from additive (±8%) to **multiplicative Q8 0.25×..2.0×** so Area / PeakHeight span ~a decade on a log axis.
- Switched baseline jitter from additive (±32 ADC) to **multiplicative Q8 0.25×..2.0×** for the same reason.
- Per-sample noise widened to ±64 counts.
- `peak_width` re-defined as **"area above half-max"** (Σ of `sample - threshold` over above-threshold samples) instead of count of above-threshold samples (which was bounded 0..32 and unreadable on a log axis).
- `peak_height` reported as **PHA convention** (peak − baseline) instead of raw ADC sample, so the 4095 ADC ceiling doesn't compress the upper tail of the distribution.

### Architecture refactor
- `MVVM`: renamed `PlotPresenter` → `PlotViewModel` for naming consistency.
- Removed dead types: `EventFrame`, `AnalysisFrame`, `RingBuffer<Event>`, `SelectionStrategy.TryExtract*`. Frame types now inherit `ProcessedData` directly.
- `IRenderTarget.Clear()` interface method added (default no-op) so the rendering engine can wipe visuals on Clear Memory without coupling to PlotItem.
- `IPlotProcessor.ForgetAll()` interface method added for the same reason on the processor side.
- `IPipelineSession.ClearMemory()` exposes the cascade to the VM.

### Diagnostics
- Stats panel shows total events, event rate, and **per-PlotType average processing + rendering ms**.
- Per-plot-type metrics aggregate across all instances (e.g. 240 histograms → one "Histogram" row).

## Pre-1.0 highlights (chronological)

- Initial telemetry pipeline scaffolding: `SerialReader`, `SerialProducer`, `BufferConsumer`, `RingBuffer<Event>`.
- `ProcessingEngine` + `RenderingEngine` with metrics tracking.
- Refactored to binary frame format (was text); `EventParser` removed.
- Multi-channel support added to `Event` model.
- Baud rate raised to 2 Mbaud.
- Plot types added one at a time: Histogram → Oscilloscope multi-channel → Pseudocolor → Spectral Ribbon.
- Worksheet placement / drag / resize / context menus.
- Channel catalog + selection strategy.
- DI container (`Microsoft.Extensions.Hosting`) wired into `App.OnStartup`.
- `DynamicBitmap` introduced as the data-layer surface separated from ScottPlot's chrome.
