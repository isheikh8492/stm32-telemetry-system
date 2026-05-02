namespace Telemetry.Core.Models
{
    public sealed record Event(
        uint EventId,
        uint TimestampMs,
        IReadOnlyList<Channel> Channels
    );

    public sealed record Channel(
        int ChannelId,
        IReadOnlyList<ushort> Samples,
        EventParameters Parameters
    );

    public sealed record EventParameters(
        ushort Baseline,
        uint Area,
        uint PeakWidth,
        ushort PeakHeight
    );
}
