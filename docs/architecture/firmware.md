# Firmware architecture

Single source: [`firmware/stm32-f446ze/Core/Src/main.c`](../../firmware/stm32-f446ze/Core/Src/main.c). Everything else under `Core/` is HAL-generated.

## Hardware

- **MCU**: STM32F446ZE (Nucleo-144).
- **Transport**: USART3 → ST-Link VCP → host USB. **2,000,000 baud**, 8-N-1, no flow control.
- **System clock**: HSI (16 MHz). The frame budget is dominated by UART throughput, not CPU.

## What it transmits

A continuous stream of fixed-size **events**. Each event = 60 channels × 32 ADC samples + 4 derived parameters per channel (baseline, area, peak width, peak height). One event ≈ 4.6 KB on the wire.

```
EVENT_CHANNEL_COUNT = 60
EVENT_SAMPLE_COUNT  = 32
FRAME_BYTES         = 14 + 60*32*2 + 60*12 = 4,574
```

At 2 Mbaud (≈200 KB/s after start/stop bits), the firmware can sustain ~40-45 events/sec. The viewer's stats panel shows the live rate.

## Channel identity is permanent

Each channel `c ∈ [0, 60)` is hard-bound at boot to one **physics-pulse shape** drawn from a 7-shape rotation:

| Shape | Source style |
|---|---|
| `SHAPE_GAUSSIAN`    | CR-RC shaper |
| `SHAPE_FAST_PULSE`  | PMT/SiPM-like, sharp rise + exp decay |
| `SHAPE_PREAMP`      | Charge-sensitive preamp: step + slow RC tail |
| `SHAPE_BIPOLAR`     | CR-RC² shaper (derivative of Gaussian) |
| `SHAPE_PILEUP`      | Two close pulses |
| `SHAPE_DAMPED_OSC`  | Detector ringing |
| `SHAPE_SATURATED`   | Wide pulse intentionally above ADC range |

`channel_shape(c) = physics_shapes[c % 7]`. Channel 0 is always Gaussian; channel 7 is always Gaussian; etc. Per-channel **baseline (1400-1600 ADC)** and **amplitude (1600-3000 ADC)** are also fixed at boot.

## Event generation: LUT + jitter

Cleanly separating "channel identity" from "per-event variation" keeps the hot path cheap and the resulting distributions interpretable.

### Boot-time LUT
`generate_lut()` renders one clean copy of every channel's pulse shape into `lut_samples[1][60*32*2]` — 3.6 KB of pre-rendered ADC values. Done once.

### Per-event hot path: `apply_jitter_and_compute_params()`
For each channel, in order:

1. **Pull amp scale** — Q8 fixed-point, `64..512` (0.25× to 2.0×). Multiplicative; spreads Area / PeakHeight across roughly a decade on a log axis.
2. **Pull baseline scale** — Q8, same range, multiplicative. Spreads Baseline across decades.
3. **Pass 1 — sample loop**: subtract original baseline → scale signal by `amp_q8 >> 8` → add jittered baseline → add per-sample noise (`±64` ADC counts) → clamp to `[0, 4095]`. Track running max for `peak_height`. Sum samples above `event_baseline` for `area`.
4. **Pass 2 — peak width**: half-max threshold = `event_baseline + (peak_height - event_baseline)/2`. `peak_width = Σ (sample - threshold)` for samples above the threshold — this is "area above half-max" (real instruments call it FWHM × amplitude). Spreads across multiple decades on a log axis.
5. **Convert peak_height to PHA convention**: `peak_height = max(0, peak_height - event_baseline)` so the ADC ceiling at 4095 doesn't compress the upper tail.
6. **Memcpy params block** into the outgoing frame.

### Why this design
- LUT keeps the inner loop branch-free.
- xorshift32 PRNG (`prng_next()`) is ~10× faster than `rand()`.
- Multiplicative jitter on amp + baseline is the only way to get distributions that fill a log axis from physically-bounded ADC values.
- Recomputing params from jittered samples (rather than pre-storing them) is what lets every event report a *different* baseline / area / peak — without that, histograms collapse to single bins.

## Main loop

```
while (1) {
  apply_jitter_and_compute_params(frame);  // 60 channels × 32 samples
  memcpy(frame + 2, &event_id, 4);
  memcpy(frame + 6, &timestamp, 4);
  HAL_UART_Transmit(&huart3, frame, FRAME_BYTES, HAL_MAX_DELAY);
  event_id++;
}
```

`HAL_UART_Transmit` is blocking — when the host is fast enough (DMA-driven serial port read on Windows), it backpressures naturally and we stay at the ~40 ev/s sustained rate. No double-buffering, no DMA TX needed at this rate.

## Tuning the jitter

If distributions look too narrow or too wide, the three knobs are all in `apply_jitter_and_compute_params`:

- `amp_q8 = 64 + (prng_next() % 449)` — widen by raising the upper bound, narrow by lowering it.
- `base_q8 = 64 + (prng_next() % 449)` — same shape.
- `noise / 2` (sample noise scale) — divisor sets the ±range; `/ 2` ≈ ±64 counts.

Real spectroscopy hardware has wider amp ranges (decades) than 0.25-2.0×; pushing this up to ~10× starts to look like real PHA distributions.
