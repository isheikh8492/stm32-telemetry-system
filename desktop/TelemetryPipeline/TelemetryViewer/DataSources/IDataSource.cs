using Telemetry.Core.Models;

namespace TelemetryViewer;

// Read-side contract that engines and plots consume. Implementations adapt
// any backing store (live RingBuffer, recorded file replay, DB query, network
// stream, ...) to this shape. Lives in TelemetryViewer because it's a
// viewer-shaped abstraction — the underlying buffer/store doesn't know about it.
public interface IDataSource
{
    int Count { get; }
    long TotalAppended { get; }
    uint? LatestEventId { get; }

    // O(1), no allocation. For plots that only need the most recent event.
    Event? PeekLatest();

    // O(N), allocates. For plots that need history (histograms, etc.).
    IReadOnlyList<Event> Snapshot();
}
