namespace Telemetry.Viewer.Services.Pipeline.Processors;

// Shared colormap implementations for pseudocolor / spectral-ribbon /
// other heat-style plots. The Turbo LUT is precomputed once at startup —
// painters do a single byte[] indexed read per cell instead of running
// the piecewise-linear interpolation each time.
public static class Colormaps
{
    // Turbo (Mikhailov 2019), 8-stop piecewise linear approximation of the
    // published LUT. Blue → cyan → green → yellow → red → dark red.
    private static readonly (double t, byte r, byte g, byte b)[] TurboStops =
    {
        (0.00,  48,  18,  59),
        (0.13,  70,  95, 230),
        (0.27,   0, 220, 255),
        (0.40,   0, 255,  95),
        (0.55, 160, 255,  30),
        (0.69, 255, 215,  30),
        (0.83, 255,  75,  25),
        (1.00, 122,   4,   3),
    };

    // Pre-baked palette: 256 entries × 4 bytes (B, G, R, A in Pbgra32 order).
    // Indexed by (int)(t * 255) clamped to [0, 255]. One memory read per pixel.
    public const int PaletteSize = 256;
    public static readonly byte[] TurboPalette = BuildTurboPalette();

    private static byte[] BuildTurboPalette()
    {
        var lut = new byte[PaletteSize * 4];
        for (int i = 0; i < PaletteSize; i++)
        {
            var t = i / (double)(PaletteSize - 1);
            InterpolateTurbo(t, out var r, out var g, out var b);
            lut[i * 4 + 0] = b;
            lut[i * 4 + 1] = g;
            lut[i * 4 + 2] = r;
            lut[i * 4 + 3] = 255;
        }
        return lut;
    }

    private static void InterpolateTurbo(double t, out byte r, out byte g, out byte b)
    {
        if (t <= TurboStops[0].t) { var s = TurboStops[0]; r = s.r; g = s.g; b = s.b; return; }
        if (t >= TurboStops[^1].t) { var s = TurboStops[^1]; r = s.r; g = s.g; b = s.b; return; }
        for (int i = 1; i < TurboStops.Length; i++)
        {
            var hi = TurboStops[i];
            if (t > hi.t) continue;
            var lo = TurboStops[i - 1];
            var f = (t - lo.t) / (hi.t - lo.t);
            r = (byte)(lo.r + (hi.r - lo.r) * f);
            g = (byte)(lo.g + (hi.g - lo.g) * f);
            b = (byte)(lo.b + (hi.b - lo.b) * f);
            return;
        }
        r = 0; g = 0; b = 0;
    }

    // Convenience for processors that still want a single Pbgra32 uint
    // (used by FillRect callers).
    public static uint Turbo(double t)
    {
        var idx = Math.Clamp((int)(t * (PaletteSize - 1)), 0, PaletteSize - 1);
        var off = idx * 4;
        return PixelCanvas.Pack(TurboPalette[off + 2], TurboPalette[off + 1], TurboPalette[off + 0], TurboPalette[off + 3]);
    }
}
