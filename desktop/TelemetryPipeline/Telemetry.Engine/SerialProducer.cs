using System.Threading.Channels;
using Telemetry.Core.Models;
using Telemetry.IO;
using ChannelFactory = System.Threading.Channels.Channel;

namespace Telemetry.Engine;

public sealed class SerialProducer : IProducer, IDisposable
{
    private readonly SerialReader _reader;
    private readonly System.Threading.Channels.Channel<Event> _channel;
    private Task? _readerTask;
    private bool _started;

    public SerialProducer(SerialReader reader, int channelCapacity = 1024)
    {
        _reader = reader;
        _channel = ChannelFactory.CreateBounded<Event>(new BoundedChannelOptions(channelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true,
        });
    }

    public ChannelReader<Event> Reader => _channel.Reader;

    public void Start()
    {
        if (_started)
            return;
        _started = true;
        _reader.EventReceived += OnEventReceived;
        _readerTask = Task.Run(_reader.Start);
    }

    public void Stop()
    {
        if (!_started)
            return;
        _started = false;
        _reader.EventReceived -= OnEventReceived;
        _reader.Stop();
        _readerTask = null;

        // Drain leftover events so a restart doesn't replay stale data.
        while (_channel.Reader.TryRead(out _)) { }
    }

    public void Dispose() => Stop();

    private void OnEventReceived(Event evt) => _channel.Writer.TryWrite(evt);
}
