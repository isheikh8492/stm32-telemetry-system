using Telemetry.Core.Models;

namespace Telemetry.Tests.Helpers;

// Constructs Event records for tests without dragging the firmware/serial
// stack in. Each Build call creates an event with `channelCount` channels;
// per-channel param values come from the supplied delegate so a test can
// hand-craft distributions (uniform / gaussian / out-of-range / NaN-as-0
// stand-in / etc.).
internal static class EventBuilder
{
    public static Event Build(
        uint eventId,
        int channelCount,
        Func<int, EventParameters> paramFactory,
        int sampleCount = 4)
    {
        var samples = new ushort[sampleCount];
        var channels = new Channel[channelCount];
        for (int c = 0; c < channelCount; c++)
            channels[c] = new Channel(c, samples, paramFactory(c));
        return new Event(eventId, eventId, channels);
    }

    public static Event UniformPeakHeight(uint eventId, int channelCount, ushort peakHeight)
        => Build(eventId, channelCount, _ =>
            new EventParameters(Baseline: 1500, Area: 0, PeakWidth: 0, PeakHeight: peakHeight));
}
