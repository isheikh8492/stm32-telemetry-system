using Telemetry.Viewer.Plotting;

namespace Telemetry.Viewer.Engine;

public interface IRenderTarget
{
    void Render(ProcessedData data);
}
