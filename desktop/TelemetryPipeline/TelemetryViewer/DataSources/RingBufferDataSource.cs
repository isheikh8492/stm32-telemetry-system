using Telemetry.Core.Models;
using Telemetry.Engine;

namespace TelemetryViewer;

// Adapter exposing a Telemetry.Engine.RingBuffer through IDataSource.
// The buffer itself doesn't know about IDataSource; this thin shim translates.
public sealed class RingBufferDataSource : IDataSource
{
    private readonly RingBuffer _buffer;

    public RingBufferDataSource(RingBuffer buffer)
    {
        _buffer = buffer;
    }

    public int Count => _buffer.Count;
    public long TotalAppended => _buffer.TotalAppended;
    public uint? LatestEventId => _buffer.LatestEventId;
    public Event? PeekLatest() => _buffer.PeekLatest();
    public IReadOnlyList<Event> Snapshot() => _buffer.Snapshot();
}
