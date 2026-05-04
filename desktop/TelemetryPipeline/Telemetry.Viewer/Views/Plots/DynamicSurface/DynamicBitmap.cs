using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Telemetry.Viewer.Views.Plots.DynamicSurface;

// Per-plot bitmap layer over the WpfPlot's data rect. The UI thread does
// nothing here except WritePixels (a memcpy from byte[] into the
// WriteableBitmap's back-buffer). All per-pixel painting happens on a
// worker thread that allocates and fills its own byte[], then hands it
// here for the blit.
//
// Sync(dataArea) repositions and resizes the surface; this also publishes
// the current target pixel size for off-thread painters via TargetWidth /
// TargetHeight (volatile reads, no lock needed for snapshot consistency).
public sealed class DynamicBitmap : Image
{
    private WriteableBitmap? _bitmap;
    private int _targetWidth;
    private int _targetHeight;

    public DynamicBitmap()
    {
        Stretch = Stretch.Fill;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment   = VerticalAlignment.Top;
        IsHitTestVisible    = false;  // pointer events fall through to DragLayer
        SnapsToDevicePixels = true;
        UseLayoutRounding   = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }

    // Latest target pixel size — readable from any thread.
    public int TargetWidth  => Volatile.Read(ref _targetWidth);
    public int TargetHeight => Volatile.Read(ref _targetHeight);

    // Position + size to match the plot's data rect (UI-thread).
    public void Sync(Rect dataArea)
    {
        Margin = new Thickness(dataArea.Left, dataArea.Top, 0, 0);
        Width  = dataArea.Width;
        Height = dataArea.Height;

        var dpi = VisualTreeHelper.GetDpi(this);
        var pxW = Math.Max(1, (int)Math.Ceiling(dataArea.Width  * dpi.DpiScaleX));
        var pxH = Math.Max(1, (int)Math.Ceiling(dataArea.Height * dpi.DpiScaleY));

        Volatile.Write(ref _targetWidth,  pxW);
        Volatile.Write(ref _targetHeight, pxH);
    }

    // UI-thread blit. Reuses the WriteableBitmap when dimensions match;
    // otherwise allocates a new one. Worker-thread paint dispatches here.
    public void PresentBitmap(byte[] buffer, int width, int height)
    {
        if (buffer is null || buffer.Length == 0 || width <= 0 || height <= 0)
            return;

        if (_bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
        {
            _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, palette: null);
            Source = _bitmap;
        }

        _bitmap.WritePixels(new Int32Rect(0, 0, width, height), buffer, width * 4, 0);
    }
}
