# Benchmarks

Measured numbers for the project. Two sources:

1. **Tests** — repeatable A/B perf assertions in [`Telemetry.Tests/Processors/`](../desktop/TelemetryPipeline/Telemetry.Tests/Processors). Run via `dotnet test` and printed to xUnit's test output. Compares each production incremental processor against a naive full-snapshot recompute reference of the same algorithm — same correctness, different per-tick cost.
2. **Live runs** — totals from the viewer's stats panel after streaming firmware data for 60 s with the default layout populated.

## Test-measured speedups

Methodology: 10K-event capacity buffer pre-filled, then time N steady-state ticks with one new event between each. Both impls receive identical inputs and produce identical pixel buffers (asserted by separate correctness tests).

| Plot type | Naive (ms/tick) | Incremental (ms/tick) | Speedup |
|---|---:|---:|---:|
| Histogram (256 bins, 1 channel)        | **1.2054** | **0.0509** | **23.7×** |
| Pseudocolor (256² bins, 2 channels)    | **4.7202** | **1.9509** |  **2.4×** |
| Spectral Ribbon (256 bins, 60 channels)| **67.1294**| **3.8313** | **17.5×** |

Source: `dotnet test` run on 2026-05-05, .NET 10, Debug build, Windows 11.
Re-run anytime — assertions enforce ≥2× speedup, actual numbers vary with hardware.

## Oscilloscope absolute throughput

Oscilloscope is stateless — `Process` reads only the latest event's raw samples and paints one polyline per selected channel. There's no across-events accumulation, so no incremental-vs-naive A/B story; instead we measure ms/tick at production-relevant channel counts. The render rate is **30 Hz** (33 ms budget); plenty of headroom.

| Channels | Samples | ms/tick |
|---:|---:|---:|
| 1   | 32 | **0.27** |
| 16  | 32 | **0.81** |
| 60  | 32 | **2.49** |

Scales roughly linearly with channel count — expected since per-tick work is `O(channels × samples)` line-segment paints.

### Why pseudocolor's speedup is smaller

Pseudocolor's per-tick cost is dominated by the **256 × 256 = 65,536 cell paint step**, which is identical between incremental and naive. Only the binning math differs. Histogram and Spectral Ribbon have proportionally cheaper paints, so the binning savings dominate. Drop pseudocolor to 64 bins and the speedup ratio rises to ~5×, but the production default is 256 — these numbers match what the user actually sees.

## Live runtime numbers (template)

Run the viewer, click **Default Layout** (1 osc + 4 ribbons + 240 histograms + 16 pseudocolors = **261 plots**), wait 60 s, capture the sidebar Stats panel:

```
2026-05-05  Default layout, 60 s warm-run
- Total events  : __,___
- Event rate    : __ ev/s
- Per-PlotType processing (avg ms):
    Oscilloscope    : ___
    Histogram       : ___
    Pseudocolor     : ___
    SpectralRibbon  : ___
- Per-PlotType rendering (avg ms):
    Oscilloscope    : ___
    Histogram       : ___
    Pseudocolor     : ___
    SpectralRibbon  : ___
- Memory (Task Manager working set): ___ MB
```

(Append a new entry every time you take a measurement so the resume numbers stay current.)

## Resume numbers worth quoting

From the test results above and the architecture itself:

| Claim | Evidence |
|---|---|
| 60-channel STM32 telemetry over 2 Mbaud UART | Firmware [`main.c`](../firmware/stm32-f446ze/Core/Src/main.c) + `SerialReader.DefaultBaudRate` |
| 4,574-byte binary frames at ~40 events/sec | Frame layout math in [`docs/architecture/serial-protocol.md`](architecture/serial-protocol.md) |
| 261 simultaneous live plots in default layout | [`Worksheet.PopulateDefaultLayout`](../desktop/TelemetryPipeline/Telemetry.Viewer/Views/Worksheet/Worksheet.cs) |
| Two-tier 30 Hz / 4 Hz render rate | [`RenderingEngine.RenderingIntervals`](../desktop/TelemetryPipeline/Telemetry.Viewer/Services/Pipeline/RenderingEngine.cs) |
| **24× faster histogram processing** vs naive recompute | `HistogramProcessorTests.Incremental_IsFasterThanNaive_AtSteadyState` |
| **17× faster 60-channel spectral ribbon** | `SpectralRibbonProcessorTests.Incremental_IsFasterThanNaive_AtSteadyState` |
| **2× faster 2D heatmap** (paint-bound at 256² bins) | `PseudocolorProcessorTests.Incremental_IsFasterThanNaive_AtSteadyState` |
| 60-channel live oscilloscope renders at **<2.5 ms/tick** (30 Hz) | `OscilloscopeProcessorTests.Throughput_AtSelectedChannelCount` |
| 25 unit + benchmark tests, all passing | `dotnet test Telemetry.Tests` |

## Reproducing

```
cd desktop/TelemetryPipeline
dotnet test Telemetry.Tests/Telemetry.Tests.csproj --logger "console;verbosity=detailed"
```

The perf tests print three lines each (`incremental: X ms/tick`, `naive: Y ms/tick`, `speedup: N×`) so you can grab the latest numbers after every run.
