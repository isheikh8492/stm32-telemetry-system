# Developer Diary

A chronological record of how this project came together — what was built, what broke, what got fixed, and what the lesson was. Entries are roughly in the order they happened (drawn from `git log` and decisions documented in code).

The point of keeping this is twofold: future-me wants to remember *why* a thing is the way it is, and anyone new to the codebase wants the story without spelunking through 47 commits.

---

## 1. Scaffolding the pipeline

First milestone: read structured data off a serial port and parse it into typed events. Built `SerialReader` (Telemetry.IO), `SerialProducer` + `BufferConsumer` (Telemetry.Engine), and a fixed-capacity `RingBuffer<Event>`. Wire format started as text — a CSV-ish framing that was easy to debug with a serial terminal.

**Lesson:** start with the simplest thing that works end-to-end before adding any abstraction. A working text protocol got me to "I see live data" in one afternoon.

## 2. Switched to a binary wire format

Text framing peaked at maybe 10 events/sec before the parser became the bottleneck. Replaced with a fixed binary frame:

```
[0xA5 0x5A][event_id u32][ts u32][channel_count u16][sample_count u16]
[samples u16[C*N]][params per channel: 12 bytes each]
```

Sync recovery via the 2-byte sync pair. Removed the old `EventParser` entirely. Used `BinaryPrimitives.ReadXXLittleEndian` for portable, span-friendly decoding.

**Lesson:** when throughput matters, decide the wire format with the host's parsing pattern in mind. `Span<byte>` + LE primitives is a 2-4× win over manual byte arithmetic.

## 3. Bumped baud rate to 2 Mbaud

ST-Link VCP can sustain 2 Mbaud reliably; defaulted there. The viewer's baud combo box still lets the user pick lower rates if needed.

## 4. Multi-channel event model

Originally the `Event` record held a single `Samples` array. Refactored to `Event(eventId, ts, IReadOnlyList<Channel>)` with each `Channel` carrying its own samples + 4 derived params (baseline, area, peakWidth, peakHeight). This turned out to be the **single most consequential model change** — it determined the shape of every plot and processor that came later.

## 5. Introduced ProcessingEngine + RenderingEngine

Two `System.Threading.Timer`-driven workers, each subclassing a shared `PollingEngine` base (interval-driven `Tick`, reentrant guard, per-PlotType timing metrics). Initial intuition: keep "compute" and "paint" decoupled so neither can starve the other.

This split paid off later when we added rate gating per plot type — the engines were already structured for it.

## 6. First plot type: Histogram

One plot type at a time. Histogram was the simplest to implement (1D bin count) and the one most useful for checking that our pipeline produced sensible distributions. Built `HistogramSettings`, `HistogramFrame`, `HistogramPlotProcessor`, `HistogramPlotItem`, plus a properties dialog.

## 7. BinCount enum

Hardcoded `int binCount` started leaking magic numbers into 5 different files. Replaced with `enum BinCount { Bins64 = 64, Bins128, Bins256, Bins512 }`. Settings store the enum, processors `(int)cast` to the value, dialogs populate combo boxes from `Enum.GetValues<BinCount>()`. One source of truth.

## 8. Oscilloscope, multi-channel

Reads the latest event's raw samples; paints one polyline per selected channel in that channel's color. Stateless — each tick is fully O(samples × channels).

## 9. Pseudocolor & Spectral Ribbon

Two more plot types added by following the same template. Pseudocolor: 2D heatmap of `(xParam, yParam)` for one or two channels. Spectral Ribbon: 60 stacked 1D histograms (one column per channel) so you can spot a drifted detector at a glance.

## 10. Channel catalog + selection strategy

Plots needed channel **names** and **colors** that the user could rename / recolor. Built `ChannelCatalog` (app-singleton, loaded from `channels.json` at startup with a 60-channel default fallback) and `SelectionStrategy(channelId, paramType)` — the seam every dialog and processor reaches through. Used a **golden-angle hue distribution** for default colors so adjacent channel ids never look similar.

## 11. Worksheet — placement, drag, resize

The thing that turns "a list of plots" into a usable tool. `Worksheet` (the VM) holds the `ObservableCollection<PlotViewModel>`. `PlotItemHost` (per-plot container) wires `DragHandler` (drag to move) + `ThumbManager` (4 corner thumbs to resize). All clicks on the empty canvas place the currently armed plot factory; clicks on a plot select+drag.

## 12. Snap-to-grid: snap the *data rect*, not the host

First implementation snapped the host's top-left to grid intersections. Looked great until you actually put plots side-by-side — the data rects (where the bars/traces live) were offset by ~30px of axis chrome, so the plots' contents were misaligned even though their *boxes* were snapped.

Fix: snap the **data rect's edge** to grid. `DragHandler`, `ThumbManager`, and `AlignToGrid` all back out the chrome offset (cached at drag start) and snap the data edge instead. After this the worksheet finally felt designed-for-grid rather than retrofitted.

**Lesson:** snap the visual element the user is *looking at*, not the bounding box the layout system happens to track.

## 13. Default Layout button

A "drop everything" button: 4 spectral ribbons + 240 histograms (60ch × 4 params) + 16 pseudocolors. Later added a full-width oscilloscope on top. Lets you press Connect → Default Layout → see the entire instrument at a glance.

## 14. The lag

Default layout populated → connect → 261 plots running concurrently → **everything ground to a halt.** Frames updated at maybe 1 Hz, the UI was barely interactive, and oscilloscope motion looked like a slideshow.

Time to figure out what was actually slow.

## 15. Pipeline analysis vs upstream Worksheet repo

Read through the upstream Worksheet repo's plot pipeline carefully and wrote `docs/worksheet-pipeline-analysis.md` cataloguing every difference. Found 9 gaps. Two were structural:

1. We were decoding the full `Event` per plot, per tick, per param — every histogram looped through the trailing window doing per-event `SelectionStrategy.TryExtract(evt, channelId, paramType)`.
2. We had no incremental state — every tick recomputed bin counts from scratch.

That's the headline lesson: at our scale, naive recompute scales `O(snapshot × plots × ticks)`. With 240 plots × 4 ticks/sec × 10K events, that's 10M binnings per second. Worker thread saturated on the binning math alone.

## 16. Refactor 1: columnar feature buffer

Replaced `RingBuffer<Event>` with `ChannelDataBuffer`: 240 parallel `double[10_000]` rings, one per (channelId, ParamType). On `Append(Event)`, the producer pulls every (channel, param) value once and writes it into the right ring. Plots' processors then read `double[]` slices directly via `GetSnapshot(featureIndex)`.

Result: read path goes from "loop events, switch on ParamType, find channel by id" to "indexed read of a contiguous double array". Single biggest CPU win on the read side.

## 17. Refactor 2: incremental processors

Each analysis processor (Histogram / Pseudocolor / Spectral Ribbon) now keeps **per-plot state**: a running `Counts[]` array plus a parallel `RingBins[]` FIFO sized to the buffer's capacity. On every tick:

1. New events since last `LastSequence`: bin them, append to RingBins, increment `Counts[bin]`.
2. If RingBins is full: pop head, decrement `Counts[evicted_bin]`.

Per-tick cost goes from `O(snapshot.Count)` to `O(events arrived since last tick)`. At steady state with 40 ev/s and 4 Hz ticks: ~10 events per tick instead of 10,000. **~1000× less work.**

## 18. The blink: ArrayPool gotcha

To save allocations, I tried renting the per-tick pixel buffer from `ArrayPool<byte>.Shared`. Plots immediately started **blinking** — random missing rows, momentary garbage, occasional all-white frames.

Root cause: the pool returned Plot A's buffer to the shared pool while Plot A's UI thread was still mid-`WritePixels`. The same buffer immediately got rented to Plot B's worker for overwrite. Cross-plot contamination, classic data race.

Reverted to `new byte[]` per tick. Allocating ~30-100 KB per tick is gen-0 GC stuff and free; the synchronization a shared pool would need to be safe is not. **Lesson:** allocation isn't always the enemy. Cheap allocations beat expensive cross-thread coordination.

## 19. The crash: cross-thread DataContext

Worker thread reading `target.Settings.Type` to decide rate gating → app crashed with a thread-affinity exception. `Settings` is a `DependencyObject`, only readable from the UI thread.

Fix: cache `PlotType` in `RenderTargetEntry` at registration time (which runs on the UI thread). Worker thread reads the cache, never the DependencyObject. **Lesson:** WPF type affinity bites in non-obvious places. If you're using `INotifyPropertyChanged` for cross-thread comms, you've usually picked the wrong tool.

## 20. The lag-after-fix-fix: SynchronizationContext.Post wasn't fast enough

Render dispatches were going through `SynchronizationContext.Post` (default `Normal` priority). UI input was elbowing them out, plots felt sluggish even though the worker had perfectly good frames ready.

Switched to `Dispatcher.BeginInvoke(DispatcherPriority.Render, ...)`. `Render` is high enough that queued blits fire ahead of idle work, but not so high that they preempt typing or button clicks. **Lesson:** in WPF, the priority you dispatch at matters as much as the fact that you dispatched.

## 21. The channel indexing bug

Subtle and ugly. After widening some firmware jitter, all the histograms suddenly showed the wrong channel's distribution — channel 5's histogram looked like channel 0's, channel 6 like channel 1, etc.

Root cause: my `ChannelDataBuffer.Append` used `evt.Channels.IndexOf(...)` (the position in the list) as the channel id. The firmware is free to send channels in any order, with gaps, etc. Position ≠ id.

Fix: index by `ch.ChannelId`, not list position. Wrote a regression test (`Append_StoresParamsByChannelId_NotByListPosition`) immediately. **Lesson:** when an integer "id" passes through a list, the test should use a list with channels in REVERSE order so position-bug is detected on day one.

## 22. Per-type rate gating (30 Hz / 4 Hz)

Oscilloscope wants live waveforms — anything below 30 Hz reads as "laggy". But histograms barely change tick to tick — 4 Hz is plenty. Built `ProcessingIntervals` and `RenderingIntervals` dictionaries on the engines:

```
[Oscilloscope]    = 33 ms       // ~30 Hz
[Histogram]       = 250 ms      // 4 Hz
[Pseudocolor]     = 250 ms
[SpectralRibbon]  = 250 ms
```

The engines tick at the base rate; per-plot `_nextProcessAt` / `_nextRenderAt` skip ticks until each plot's interval has elapsed. **Lesson:** uniform tick rates waste cycles on plots that don't need them. Per-type gating is one of the cheapest performance wins per LOC.

## 23. Renamed `PlotPresenter` → `PlotViewModel`

Naming-only refactor. The class held bindable position/size/zindex/selection plus a reference to `PlotSettings` — that's a view-model, not a presenter. `Presenter` has connotations from MVP that we don't follow. **Lesson:** patterns are vocabulary; mis-naming is a tax on every future reader.

## 24. Dead-code purge

Once the incremental processors and feature buffer were in place, several types went obsolete:

- `RingBuffer<Event>` — replaced by `ChannelDataBuffer`.
- `EventFrame` / `AnalysisFrame` — both inherited from `ProcessedData` for no reason; flattened to direct subclasses.
- `SelectionStrategy.TryExtract*` — pre-extraction at append time made the runtime extractor unnecessary.

Deleted ~5 types totaling a few hundred lines. **Lesson:** keep deleting. Code that no caller exercises is just docs that lie.

## 25. The Clear Memory button

Use case: user runs for a while, distributions look noisy, wants to start fresh without disconnecting and reconnecting. Built a cascading `ClearMemory()`:

1. `ChannelDataBuffer.Clear()` — wipes feature rings, zeros `TotalAppended`.
2. `ProcessingEngine.ClearState()` — drops fingerprints + nextProcessAt + broadcasts `IPlotProcessor.ForgetAll()`.
3. `RenderingEngine.ClearAll()` — drops pending/last-rendered + dispatches `IRenderTarget.Clear()` (UI-thread bitmap hide).
4. `PipelineStatsViewModel.Reset()` — re-baselines rate calc and zeros displayed counters.

Required adding two new interface methods (`IPlotProcessor.ForgetAll()`, `IRenderTarget.Clear()`) but the cascade is so much cleaner than any in-place patching alternative.

## 26. Firmware: jitter that actually fills a log axis

Initial firmware used `±8%` amp jitter, `±32 ADC` baseline shift, `±21 ADC` per-sample noise. Distributions on a 1..1,000,000 log axis collapsed into 1-2 bins per channel — every histogram looked like a flatline spike.

Rewrote the per-event hot path:

- Amp scale: **multiplicative Q8 0.25..2.0×** (was additive ±8%).
- Baseline scale: **multiplicative Q8 0.25..2.0×** (was additive ±32).
- Noise: ±64 ADC counts (was ±21).

Now Area + PeakHeight + Baseline span proper bell distributions across decades on the log axis.

## 27. PeakHeight clamping at 4095

Even with wider amp jitter, `peak_height` was crunched into a narrow band near 10². Cause: the raw `peak_height = max(samples)` is bounded above by the ADC ceiling (4095). With multiplicative amp scaling, every "high amp" event saturated to the same value.

Fix: report **peak above baseline** (`peak_height - event_baseline`) — the standard PHA (pulse height analysis) convention used by real spectroscopy hardware. Distribution now spans decades because amp scaling moves the *signal portion* freely without the ADC ceiling compressing the upper tail.

## 28. PeakWidth: count → area-above-half-max

`peak_width` was originally "count of samples above half-max threshold" — bounded 0..32 (`EVENT_SAMPLE_COUNT`). Useless on a log axis.

Rewrote as `Σ (sample - threshold)` for samples above the threshold — i.e. "area above half-max" (FWHM × amplitude in continuous-value terms). Spans 0..~30k naturally and reads as a proper distribution. Real instruments compute this. **Lesson:** if the metric you're plotting is bounded and your axis is log, you're plotting the wrong metric.

## 29. Visual polish

- Histogram bars switched from steel blue → **solid black**.
- Histogram Y-axis floor lowered from 1K → 100 → 10 (so empty/sparse plots don't show wasted space at the top).
- Default `BinCount` set to 256 across all binned plot types.
- Default layout: oscilloscope (channel 0, full-width) sits **above** the spectral ribbons.
- Show-grid checkbox starts off, snap-to-grid starts off — toggle works correctly without any "first-run" weirdness.

## 30. Testing strategy: A/B against a naive reference

Wrote three "naive" reference processors (full-snapshot recompute) used only by tests. Each xUnit test:

1. Asserts incremental == naive on identical inputs (correctness).
2. Asserts incremental == naive after the ring wraps (eviction-path correctness).
3. Asserts incremental is at least 2× faster at steady state (perf claim).

Single test pattern delivers correctness + a measurable perf number per release. Production processor stays clean (no test-only branches); the naive reference lives in `Telemetry.Tests/Reference/` and never ships.

**Lesson:** the best perf assertion is "we beat a known-correct reference implementation by N×". It's defensible, reproducible, and self-documenting.

## 31. Measured speedups (the payoff)

After all the above, `dotnet test` prints:

```
Histogram      incremental: 0.05 ms/tick    naive: 1.21 ms/tick   speedup: 23.7×
SpectralRibbon incremental: 3.83 ms/tick    naive: 67.13 ms/tick  speedup: 17.5×
Pseudocolor    incremental: 1.95 ms/tick    naive: 4.72 ms/tick   speedup:  2.4×
Oscilloscope (60 ch × 32 samples):                     2.49 ms/tick
```

Pseudocolor's modest speedup is real: at 256² = 65,536 cells, the **paint step** dominates and is identical between impls. That's documented in `BENCHMARKS.md` so future-me doesn't think the test is broken.

## 32. Documentation pass

End-of-project: wrote a structured docs tree:

- `README.md` — overview + quick-start + metrics.
- `docs/architecture/` — six in-depth pieces (firmware, serial protocol, data pipeline, plot types, worksheet UI, rendering).
- `docs/BENCHMARKS.md` — measured A/B speedups with methodology + live-run template.
- `docs/CHANGELOG.md` — milestone-grouped feature history.
- `docs/DEV-DIARY.md` — this file.

The point of the architecture docs isn't comprehensiveness; it's that someone new to the codebase can read one page per subsystem and have enough context to start contributing. The point of the diary is that someone reading the code six months from now can answer "why is it this way?" without spelunking through git blame.

## 33. What I'd do differently

- **Write the channel-id regression test before the bug.** Position-vs-id is a foreseeable foot-gun.
- **Skip the `ArrayPool` experiment.** I had a working solution; pooling was a premature optimization.
- **Use `Dispatcher.BeginInvoke(Render)` from day one.** `SynchronizationContext.Post` is the wrong default for rendering loops.
- **Keep the dead-code purge as a continuous discipline**, not an end-of-project cleanup. Each refactor should delete more than it adds.

## 34. What I'd build next

- **Recording / replay** — write `ChannelDataBuffer` snapshots to disk; replay them through the pipeline. Lets you debug a distribution oddity offline.
- **Channel rename / recolor UI** — currently you have to edit `channels.json` by hand.
- **More plot types** — line plot of a parameter over time (sliding 60-second window), 1D scatter for outlier hunting.
- **Better Properties dialog** — current one works but is barebones.
- **Cross-platform** — WPF locks us to Windows. Avalonia would let the same XAML run on macOS / Linux for free.
