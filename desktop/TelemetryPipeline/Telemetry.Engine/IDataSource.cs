using Telemetry.Core.Models;

namespace Telemetry.Engine;

public interface IDataSource
{
    int Count { get; }
    uint? LatestEventId { get; }
    IReadOnlyList<Event> Snapshot();
}
