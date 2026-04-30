namespace Telemetry.Core.Models
{
    public sealed record Event(
        uint EventId,
        uint TimestampMs,
        IReadOnlyList<ushort> Samples,
        EventParameters EventParameters
    );

    public sealed record EventParameters(
        ushort Baseline,
        uint Area,
        uint PeakWidth,
        ushort PeakHeight
    );
}
