namespace Telemetry.Core.Models
{
    public sealed record Event(
        uint EventId,
        uint TimestampMs,
        IReadOnlyList<ushort> Samples
    );
}
