using Telemetry.Core.Models;

namespace Telemetry.Viewer.Services.DataSources;

// Read-side contract that engines and plots consume. Implementations adapt
// any backing store (live ChannelDataBuffer, recorded file replay, DB
// query, network stream, ...) to this shape. Lives in Telemetry.Viewer
// because it's a viewer-shaped abstraction.
public interface IDataSource
{
    int Count { get; }
    int Capacity { get; }
    long TotalAppended { get; }
    uint? LatestEventId { get; }

    // O(1). For plots that only need the most recent event (Oscilloscope —
    // raw samples are not pre-extracted into feature rings).
    Event? PeekLatest();

    // Single-feature snapshot. featureIndex = ChannelDataBuffer.FeatureIndex(channelId, param).
    ChannelWindowSnapshot GetSnapshot(int featureIndex);

    // Multi-feature snapshot, single shared sequence cursor across all
    // requested features. Caller passes a pre-allocated array sized to
    // featureIndices.Count to avoid per-tick allocation.
    MultiChannelWindowSnapshot GetSnapshot(IReadOnlyList<int> featureIndices, double[][] outFeatures);
}
