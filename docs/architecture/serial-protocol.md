# Serial protocol

The wire format is a continuous stream of fixed-size **event frames**. Both ends agree on the layout statically: there's no length prefix beyond the header, no checksum, and no acknowledgment.

## Frame layout

```
Offset  Bytes  Field            Notes
------  -----  ---------------  ----------------------------------------
   0      2    SYNC             0xA5, 0x5A
   2      4    event_id (u32)   monotonic, little-endian, wraps at 2^32
   6      4    timestamp_ms     little-endian, HAL_GetTick() at TX
  10      2    channel_count    little-endian, currently 60
  12      2    sample_count     little-endian, currently 32

           HEADER = 14 bytes total (12 bytes after sync)

  14    C*N*2   samples          ushort[C][N], channel-major, little-endian
                                  C = channel_count, N = sample_count

       C*N*2 + 14
         C*12   params           per channel, 12 bytes each:
                                   u16 baseline
                                   u32 area
                                   u32 peak_width
                                   u16 peak_height

           Total = 14 + C*N*2 + C*12
                 = 14 + 60*32*2 + 60*12
                 = 4,574 bytes for the default config
```

All multi-byte integers are **little-endian** (matches both the STM32 ARM Cortex-M4 native byte order and x86 host byte order, so neither side has to swap).

## Sync recovery

The decoder hunts for `0xA5` then `0x5A`. On any read timeout (default 1 s) or partial frame, it drops back to hunting — the next sync pair re-syncs without losing more than one frame.

The sync sequence `0xA5 0x5A` was chosen because:
- It's distinctive enough to make accidental matches in payload data rare.
- Bitwise complement of each byte: a single bit flip on either won't match the other.

There is no escape mechanism — payload bytes can in principle contain `0xA5 0x5A`, in which case a corrupted resync would land mid-frame. In practice the misalignment self-corrects within one frame because the validity checks reject implausible `channel_count` / `sample_count` and we drop back to hunting.

## Validity checks (host side)

`SerialReader.Start` rejects a candidate frame if:

```
channel_count == 0  ||  channel_count > 256
sample_count  == 0  ||  sample_count  > 4096
```

Any rejected frame causes a return to sync hunting.

## Channels-major sample layout

Samples are laid out **channel-major**, not sample-major:

```
ch0_s0, ch0_s1, ..., ch0_s31,
ch1_s0, ch1_s1, ..., ch1_s31,
...
ch59_s0, ch59_s1, ..., ch59_s31
```

This matches how the firmware composes the frame (one channel at a time, contiguous) and how the host parses it (`Buffer.BlockCopy` per channel).

## Params block

Per channel, 12 bytes. Order:

```
+0   u16  baseline      ADC counts of the event's effective baseline
+2   u32  area          Σ(sample - baseline) over samples above baseline
+6   u32  peak_width    Σ(sample - threshold) over samples above half-max
                         (i.e. "area above half-max", not raw FWHM in samples)
+10  u16  peak_height   max(sample) - baseline  (PHA convention)
```

`peak_width` and `peak_height` are stored after the firmware's per-event recompute, so they reflect the jittered samples in this frame, not the LUT.

## Why no checksum / ack

- USB-virtual-COM is reliable below the framing layer (USB CRCs, retransmit).
- The link is one-way (firmware → host); there's no feedback channel that could change firmware behavior.
- Loss of one frame in 100K is invisible in distributions of 10K trailing events.
- Adding a CRC at 2 Mbaud would burn an extra 1-2 KB/s without changing the user experience.

## Endpoints

- **Firmware writer**: [`firmware/stm32-f446ze/Core/Src/main.c`](../../firmware/stm32-f446ze/Core/Src/main.c) — single `HAL_UART_Transmit` call per event with the entire frame.
- **Host reader**: [`desktop/.../Telemetry.IO/SerialReader.cs`](../../desktop/TelemetryPipeline/Telemetry.IO/SerialReader.cs) — synchronous read on a dedicated thread, fires `EventReceived` per validated frame.
