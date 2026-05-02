using System.Threading.Channels;
using Telemetry.Core.Models;
using Telemetry.Engine;

namespace Telemetry.IO;

public sealed class SerialProducer : IProducer, IDisposable
{
    private readonly SerialReader _reader;
    private readonly Channel<Event> _channel;
    private Task? _readerTask;

    public SerialProducer(SerialReader reader, int channelCapacity = 1024)
    {
        _reader = reader;
        _channel = Channel.CreateBounded<Event>(new BoundedChannelOptions(channelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true,
        });
        _reader.EventReceived += OnEventReceived;
    }

    public ChannelReader<Event> Reader => _channel.Reader;

    public void Start()
    {
        if (_readerTask is not null)
            return;
        _readerTask = Task.Run(_reader.Start);
    }

    public void Stop()
    {
        _reader.Stop();
        _readerTask = null;

        // Drain leftover events so a restart doesn't replay stale data.
        while (_channel.Reader.TryRead(out _)) { }
    }

    public void Dispose()
    {
        _reader.EventReceived -= OnEventReceived;
        Stop();
    }

    private void OnEventReceived(Event evt) => _channel.Writer.TryWrite(evt);
}
