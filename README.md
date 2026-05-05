# STM32 Telemetry System

End-to-end multi-channel telemetry pipeline: STM32F446ZE firmware streams 60-channel pulse-shape data over UART; a WPF desktop viewer parses, buffers, processes, and renders it in real time across an interactive plot worksheet.

The viewer holds **260 plots at zero lag** on commodity hardware (1 oscilloscope + 4 spectral ribbons + 240 histograms + 16 pseudocolors) by separating worker-thread compute from UI-thread blits, gating per-plot updates by type, and keeping incremental state so per-tick work is *O(events arrived since last tick)* rather than *O(snapshot)*.

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
- [docs/CHANGELOG.md](docs/CHANGELOG.md) — feature history
- [docs/worksheet-pipeline-analysis.md](docs/worksheet-pipeline-analysis.md) — original "what makes the upstream Worksheet repo zero-lag" analysis that drove the refactor

## Tech stack

- **Firmware**: C, STM32 HAL, USART3 @ 2 Mbaud, xorshift32 PRNG, fixed-point Q8 amp jitter
- **Desktop**: .NET 10, WPF, ScottPlot 5, `Microsoft.Extensions.Hosting` DI
- **Threading**: `System.Threading.Channels` between producer/consumer; `System.Threading.Timer` polling engines; `Dispatcher.BeginInvoke(Render)` for UI blits

## License

See firmware files for ST's BSD-style license. Desktop code: TBD.
