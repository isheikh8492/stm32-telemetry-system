using System.Threading;
using Telemetry.Engine;
using Telemetry.IO;
using Telemetry.Viewer.Services.DataSources;

namespace Telemetry.Viewer.Services.Pipeline;

public sealed class PipelineFactory : IPipelineFactory
{
    private const int BufferCapacity = 10_000;

    private readonly SynchronizationContext _uiContext;

    public PipelineFactory()
    {
        _uiContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("PipelineFactory must be constructed on the UI thread.");
    }

    public IPipelineSession Create(string portName, int baudRate)
    {
        var reader = new SerialReader(portName, baudRate);
        var producer = new SerialProducer(reader);
        var buffer = new RingBuffer(BufferCapacity);
        var consumer = new BufferConsumer(producer.Reader, buffer);
        var dataSource = new RingBufferDataSource(buffer);
        var viewport = new ViewportSession(dataSource, _uiContext);

        return new PipelineSession(reader, producer, consumer, buffer, viewport, _uiContext);
    }
}
