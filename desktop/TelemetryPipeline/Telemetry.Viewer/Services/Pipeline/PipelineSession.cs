using System.Threading;
using System.Threading.Tasks;
using Telemetry.Engine;
using Telemetry.IO;

namespace Telemetry.Viewer.Services.Pipeline;

public sealed class PipelineSession : IPipelineSession
{
    private readonly SerialReader _reader;
    private readonly SerialProducer _producer;
    private readonly BufferConsumer _consumer;
    private readonly RingBuffer _buffer;
    private readonly ViewportSession _viewport;
    private readonly SynchronizationContext _uiContext;

    private CancellationTokenSource? _consumerCts;
    private Task? _consumerTask;
    private bool _disposed;

    public PipelineSession(
        SerialReader reader,
        SerialProducer producer,
        BufferConsumer consumer,
        RingBuffer buffer,
        ViewportSession viewport,
        SynchronizationContext uiContext)
    {
        _reader = reader;
        _producer = producer;
        _consumer = consumer;
        _buffer = buffer;
        _viewport = viewport;
        _uiContext = uiContext;

        _reader.ErrorOccurred += OnReaderError;
    }

    public RingBuffer Buffer => _buffer;
    public ViewportSession Viewport => _viewport;

    public event Action<string>? ErrorOccurred;

    public void Start()
    {
        // Order matters: consumer task first (so it's awaiting), then producer
        // (which feeds), then engines.
        _consumerCts = new CancellationTokenSource();
        _consumerTask = Task.Run(() => _consumer.RunAsync(_consumerCts.Token));
        _producer.Start();
        _viewport.Start();
    }

    public void Stop()
    {
        _viewport.Stop();
        _consumerCts?.Cancel();
        _producer.Stop();
    }

    // SerialReader fires this on a worker thread; we marshal to the UI thread
    // so VM subscribers can update bindings or show dialogs without ceremony.
    private void OnReaderError(string message)
    {
        _uiContext.Post(_ => ErrorOccurred?.Invoke(message), null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _reader.ErrorOccurred -= OnReaderError;

        _viewport.Dispose();
        _consumerCts?.Cancel();
        _producer.Dispose();
        _reader.Dispose();
        _consumerCts?.Dispose();
    }
}
