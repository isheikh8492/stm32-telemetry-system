using Telemetry.Core.Models;
using Telemetry.Viewer.Services.DataSources;

namespace Telemetry.Tests.Helpers;

// Wraps a ChannelDataBuffer in the production RingBufferDataSource adapter so
// tests exercise the same IDataSource surface ProcessingEngine uses.
internal static class BufferFactory
{
    public static (ChannelDataBuffer Buffer, IDataSource Source) Make(int capacity, int channelCount)
    {
        var buffer = new ChannelDataBuffer(capacity, channelCount);
        var source = new RingBufferDataSource(buffer);
        return (buffer, source);
    }

    public static void FillWith(ChannelDataBuffer buffer, IEnumerable<Event> events)
    {
        foreach (var e in events)
            buffer.Append(e);
    }
}
