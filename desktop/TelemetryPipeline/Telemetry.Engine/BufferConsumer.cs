using System.Threading.Channels;
using Telemetry.Core.Models;

namespace Telemetry.Engine;

public sealed class BufferConsumer : IConsumer
{
    private readonly ChannelReader<Event> _reader;
    private readonly IEventBuffer _buffer;

    public BufferConsumer(ChannelReader<Event> reader, IEventBuffer buffer)
    {
        _reader = reader;
        _buffer = buffer;
    }

    public async Task RunAsync(CancellationToken token)
    {
        await foreach (var evt in _reader.ReadAllAsync(token).ConfigureAwait(false))
            _buffer.Append(evt);
    }
}
