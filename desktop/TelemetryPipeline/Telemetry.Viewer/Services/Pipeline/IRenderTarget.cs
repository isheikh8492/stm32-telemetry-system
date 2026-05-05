using System.Windows;
using Telemetry.Viewer.Models;

namespace Telemetry.Viewer.Services.Pipeline;

// Pipeline's view of a renderable plot. Decouples the renderer from the
// concrete WPF UserControl — anything that can: identify itself, expose its
// settings, report its current pixel-buffer target size + raise a change
// event, and accept a ProcessedData on the UI thread, can be a render
// target. PlotItem implements this; tests can fake it.
public interface IRenderTarget
{
    Guid Id { get; }
    PlotSettings Settings { get; }

    int PixelWidth { get; }
    int PixelHeight { get; }

    // Fires when the plot's data rect changes (after a layout / DPI / resize).
    // The session subscribes to this and republishes pixel size to the store.
    event Action<Rect>? DataAreaChanged;

    // Called on the UI thread by RenderingEngine with the latest processed
    // frame. Implementations blit data.Buffer onto their bitmap surface.
    void Render(ProcessedData data);

    // Wipe whatever the target is currently displaying. Called on the UI
    // thread when the user clears the in-memory buffer so plots stop showing
    // stale frames.
    void Clear() { }
}
