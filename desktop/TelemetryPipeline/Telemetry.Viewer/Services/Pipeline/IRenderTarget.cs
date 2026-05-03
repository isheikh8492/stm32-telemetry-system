using Telemetry.Viewer.Models;

namespace Telemetry.Viewer.Services.Pipeline;

public interface IRenderTarget
{
    void Render(ProcessedData data);
}
