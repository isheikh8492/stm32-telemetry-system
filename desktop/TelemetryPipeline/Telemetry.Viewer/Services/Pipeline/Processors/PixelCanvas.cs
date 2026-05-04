namespace Telemetry.Viewer.Services.Pipeline.Processors;

// Pbgra32 pixel primitives. Buffer layout: width*height*4 bytes, B,G,R,A per pixel.
// All methods are clip-safe — out-of-bounds coordinates are silently ignored.
//
// Static helpers, not a dispatcher concern — kept out of IPlotProcessor so
// processors can pull in only the primitives they need.
public static class PixelCanvas
{
    // Pbgra32 colors (premultiplied, opaque).
    public static readonly uint SteelBlue = Pack(70, 130, 180, 255);

    public static uint Pack(byte r, byte g, byte b, byte a)
        => (uint)((a << 24) | (r << 16) | (g << 8) | b);

    public static void Pixel(byte[] buffer, int width, int height, int x, int y, uint bgra)
    {
        if ((uint)x >= (uint)width || (uint)y >= (uint)height) return;
        var i = (y * width + x) * 4;
        buffer[i + 0] = (byte)(bgra);
        buffer[i + 1] = (byte)(bgra >> 8);
        buffer[i + 2] = (byte)(bgra >> 16);
        buffer[i + 3] = (byte)(bgra >> 24);
    }

    public static void FillRect(byte[] buffer, int width, int height, int x, int y, int w, int h, uint bgra)
    {
        if (w <= 0 || h <= 0) return;
        var x0 = Math.Max(0, x);
        var y0 = Math.Max(0, y);
        var x1 = Math.Min(width,  x + w);
        var y1 = Math.Min(height, y + h);
        if (x1 <= x0 || y1 <= y0) return;

        byte b = (byte)(bgra);
        byte g = (byte)(bgra >> 8);
        byte r = (byte)(bgra >> 16);
        byte a = (byte)(bgra >> 24);

        for (int yy = y0; yy < y1; yy++)
        {
            int row = (yy * width + x0) * 4;
            for (int xx = x0; xx < x1; xx++)
            {
                buffer[row + 0] = b;
                buffer[row + 1] = g;
                buffer[row + 2] = r;
                buffer[row + 3] = a;
                row += 4;
            }
        }
    }

    // Bresenham. Single-pixel line; off-buffer points are clipped per Pixel().
    public static void Line(byte[] buffer, int width, int height, int x0, int y0, int x1, int y1, uint bgra)
    {
        int dx = Math.Abs(x1 - x0);
        int dy = -Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            Pixel(buffer, width, height, x0, y0, bgra);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }
}
