using Telemetry.Core.Models;

namespace Telemetry.Engine;

public interface IDataSource
{
    int Count { get; }
    uint? LatestEventId { get; }

    // O(1), no allocation. For plots that only need the most recent event.
    Event? PeekLatest();

    // O(N), allocates. For plots that need history (histograms, etc.).
    IReadOnlyList<Event> Snapshot();
}
