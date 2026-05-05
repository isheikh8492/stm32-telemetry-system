# STM32 Telemetry System

End-to-end multi-channel telemetry pipeline: STM32F446ZE firmware streams 60-channel pulse-shape data over UART; a WPF desktop viewer parses, buffers, processes, and renders it in real time across an interactive plot worksheet.

The viewer holds **261 plots at zero lag** on commodity hardware (1 oscilloscope + 4 spectral ribbons + 240 histograms + 16 pseudocolors) by separating worker-thread compute from UI-thread blits, gating per-plot updates by type, and keeping incremental state so per-tick work is *O(events arrived since last tick)* rather than *O(snapshot)*.

## Metrics

All numbers measured by the [test suite](desktop/TelemetryPipeline/Telemetry.Tests) (`dotnet test Telemetry.Tests`). Re-run anytime — assertions enforce the speedup floors. See [docs/BENCHMARKS.md](docs/BENCHMARKS.md) for full methodology.

### Scale & throughput

| | |
|---|---|
| Channels per event              | **60** |
| Samples per channel per event   | **32** |
| Derived params per channel      | **4** (baseline, area, peak width, peak height) |
| Frame size                      | **4,574 bytes** |
| UART link                       | **2 Mbaud** (USART3 → ST-Link VCP) |
| Sustained event rate            | **~40 events/sec** end-to-end |
| Trailing-window buffer          | **10,000 events** across 240 feature rings (~19 MB) |
| Default-layout plots            | **261 simultaneous live plots** |
| Render rates                    | **30 Hz** oscilloscope · **4 Hz** analytics (rate-gated per-type) |

### Per-plot processing speedups

Incremental ring-FIFO binning vs naive full-snapshot recompute, identical inputs, pixel-perfect-equal outputs:

| Plot type | Naive | Incremental | Speedup |
|---|---:|---:|---:|
| Histogram (256 bins, 1 channel)         | 1.21 ms | **0.05 ms** | **23.7×** |
| Spectral Ribbon (256 bins, 60 channels) | 67.13 ms| **3.83 ms** | **17.5×** |
| Pseudocolor (256² bins)                 | 4.72 ms | **1.95 ms** |  **2.4×** (paint-bound) |

### Oscilloscope absolute throughput

Stateless processor; per-tick cost scales linearly with selected channels. 30 Hz render budget = 33 ms per tick:

| Selected channels | ms/tick |
|---:|---:|
| 1   | **0.27 ms** |
| 16  | **0.81 ms** |
| 60  | **2.49 ms** |

### Architecture highlights

- **5-stage producer/consumer pipeline**: `SerialReader → Channel<Event> → BufferConsumer → ChannelDataBuffer → ProcessingEngine → RenderingEngine` with bounded backpressure and coalesced UI dispatches.
- **240-ring columnar feature buffer** pre-extracts `(channelId, ParamType)` values at append time — plots read `double[]` slices directly, no per-event lookups.
- **Two-tier render rate gating** (30 Hz oscilloscope / 4 Hz analytics) so the pipeline doesn't waste cycles producing frames the user can't perceive.
- **Off-thread paint, on-thread blit**: worker threads paint into fresh per-tick `byte[]` Pbgra32 buffers; UI thread does only `WriteableBitmap.WritePixels` blits via `Dispatcher.BeginInvoke(DispatcherPriority.Render)`.
- **Pluggable plot-type registry** — adding a new plot type is 5 files, no engine changes.
- **MVVM + DI** via `Microsoft.Extensions.Hosting` with dependency-inverted `IDataSource` / `IRenderTarget` / `IPlotProcessor` seams.

### Firmware

- Hot path in **branch-free C** with **xorshift32 PRNG** and **Q8 fixed-point** multiplicative jitter — no `rand()`, no float divisions in the inner loop.
- LUT-rendered channel shapes (boot-time) decoupled from per-event amplitude / baseline / noise jitter, so distributions span decades on a log axis.
- 60 channels × 32 samples × 4 recomputed params per event, all packaged inside one `HAL_UART_Transmit` call.

### Test suite

**25 tests** — processor correctness (incremental == naive on full capacity AND after ring wrap), processor performance (≥2× speedup floors), ring-buffer semantics (channel-id indexing, clear, snapshot wrap, NaN for missing channels), and pure-function edge cases (Y-axis NiceMax). All passing.

## Repository layout

```
firmware/stm32-f446ze/         STM32 HAL project (Core/, Drivers/, .ioc, .ld)
desktop/TelemetryPipeline/
  Telemetry.Core/              Wire-format DTOs (Event, Channel, EventParameters)
  Telemetry.IO/                SerialReader, port discovery
  Telemetry.Engine/            Producer/consumer + buffer abstraction
  Telemetry.Viewer/            WPF app — worksheet, plots, pipeline engines
  Telemetry.ConsoleTest/       Headless smoke test
docs/                          Architecture docs + changelog
```

## Quick start

### Firmware
1. Open `firmware/stm32-f446ze/stm32_telemetry_platform.ioc` in STM32CubeIDE.
2. Build, flash to an STM32F446ZE Nucleo board.
3. USART3 (ST-Link VCP) streams at **2 Mbaud**.

### Viewer
1. Open `desktop/TelemetryPipeline/TelemetryPipeline.slnx` in Visual Studio (or `dotnet build`) — requires .NET 10 SDK.
2. Run **Telemetry.Viewer** — Windows-only (WPF).
3. Pick the COM port + baud, click **Connect**, then **Default Layout** to drop a full set of plots.

## Documentation

- [docs/architecture/firmware.md](docs/architecture/firmware.md) — STM32 frame generation, jitter model, LUT layout
- [docs/architecture/serial-protocol.md](docs/architecture/serial-protocol.md) — wire format, sync bytes, framing rules
- [docs/architecture/data-pipeline.md](docs/architecture/data-pipeline.md) — producer/consumer/buffer/processing/rendering
- [docs/architecture/plot-types.md](docs/architecture/plot-types.md) — Oscilloscope, Histogram, Pseudocolor, Spectral Ribbon
- [docs/architecture/worksheet-ui.md](docs/architecture/worksheet-ui.md) — placement, drag, resize, snap-to-grid, default layout
- [docs/architecture/rendering.md](docs/architecture/rendering.md) — DynamicBitmap blit path, ScottPlot chrome, off-thread paint
- [docs/BENCHMARKS.md](docs/BENCHMARKS.md) — measured A/B speedups (incremental vs naive recompute) + live-run template
- [docs/CHANGELOG.md](docs/CHANGELOG.md) — feature history
- [docs/worksheet-pipeline-analysis.md](docs/worksheet-pipeline-analysis.md) — original "what makes the upstream Worksheet repo zero-lag" analysis that drove the refactor

## Tech stack

- **Firmware**: C, STM32 HAL, USART3 @ 2 Mbaud, xorshift32 PRNG, fixed-point Q8 amp jitter
- **Desktop**: .NET 10, WPF, ScottPlot 5, `Microsoft.Extensions.Hosting` DI
- **Threading**: `System.Threading.Channels` between producer/consumer; `System.Threading.Timer` polling engines; `Dispatcher.BeginInvoke(Render)` for UI blits

## License

See firmware files for ST's BSD-style license. Desktop code: TBD.
