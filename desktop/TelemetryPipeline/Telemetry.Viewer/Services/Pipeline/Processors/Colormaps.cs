namespace Telemetry.Viewer.Services.Pipeline.Processors;

// Shared colormap implementations for pseudocolor / spectral-ribbon /
// other heat-style plots. Returns Pbgra32 uint colors ready for
// PixelCanvas.FillRect.
public static class Colormaps
{
    // Turbo (Mikhailov 2019), 8-stop piecewise linear approximation of the
    // published LUT. Blue → cyan → green → yellow → red → dark red.
    // t in [0, 1]; values outside are clamped.
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

    public static uint Turbo(double t)
    {
        if (t <= TurboStops[0].t) { var s = TurboStops[0]; return PixelCanvas.Pack(s.r, s.g, s.b, 255); }
        if (t >= TurboStops[^1].t) { var s = TurboStops[^1]; return PixelCanvas.Pack(s.r, s.g, s.b, 255); }
        for (int i = 1; i < TurboStops.Length; i++)
        {
            var hi = TurboStops[i];
            if (t > hi.t) continue;
            var lo = TurboStops[i - 1];
            var f = (t - lo.t) / (hi.t - lo.t);
            return PixelCanvas.Pack(
                (byte)(lo.r + (hi.r - lo.r) * f),
                (byte)(lo.g + (hi.g - lo.g) * f),
                (byte)(lo.b + (hi.b - lo.b) * f),
                255);
        }
        return 0xFF000000;
    }
}
