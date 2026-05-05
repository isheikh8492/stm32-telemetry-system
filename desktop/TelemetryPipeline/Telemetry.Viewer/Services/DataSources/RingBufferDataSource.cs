using Telemetry.Core.Models;

namespace Telemetry.Viewer.Services.DataSources;

// Adapter exposing a ChannelDataBuffer through IDataSource.
public sealed class RingBufferDataSource : IDataSource
{
    private readonly ChannelDataBuffer _buffer;

    public RingBufferDataSource(ChannelDataBuffer buffer)
    {
        _buffer = buffer;
    }

    public int Count => _buffer.Count;
    public int Capacity => _buffer.Capacity;
    public long TotalAppended => _buffer.TotalAppended;
    public uint? LatestEventId => _buffer.LatestEventId;
    public Event? PeekLatest() => _buffer.PeekLatest();

    public ChannelWindowSnapshot GetSnapshot(int featureIndex)
        => _buffer.GetSnapshot(featureIndex);

    public MultiChannelWindowSnapshot GetSnapshot(IReadOnlyList<int> featureIndices, double[][] outFeatures)
        => _buffer.GetSnapshot(featureIndices, outFeatures);
}
