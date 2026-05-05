namespace Telemetry.Viewer.Services.DataSources;

// A read-only view over a single feature's ring at a moment in time. The
// `Values` array is the buffer's internal storage — no copy. Caller indexes
// by sequence; physical slot = sequence % Capacity. Producer overwrites
// slots in place, so reads while the producer is appending are eventually
// consistent (no protection against torn double writes; acceptable for the
// per-cell delta granularity histograms care about).
public readonly record struct ChannelWindowSnapshot(
    double[] Values,
    int Count,
    int Capacity,
    long StartSequence,
    long EndSequence)
{
    public double At(long sequence) => Values[(int)(sequence % Capacity)];
    public bool IsEmpty => Count == 0;
}

// Same idea but for plots that need multiple features synchronized
// (Pseudocolor X+Y, Spectral Ribbon's many channels). `Features[i]` is the
// i-th requested feature's ring.
public readonly record struct MultiChannelWindowSnapshot(
    double[][] Features,
    int Count,
    int Capacity,
    long StartSequence,
    long EndSequence)
{
    public double At(int featureSlot, long sequence)
        => Features[featureSlot][(int)(sequence % Capacity)];
    public bool IsEmpty => Count == 0;
    public int FeatureCount => Features.Length;
}
