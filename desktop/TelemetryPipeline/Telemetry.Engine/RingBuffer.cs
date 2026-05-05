using Telemetry.Core.Models;

namespace Telemetry.Engine;

public sealed class RingBuffer : IEventBuffer
{
    private readonly Event[] _events;
    private readonly object _lock = new();
    private int _writeIndex;
    private int _count;
    private long _totalAppended;

    public RingBuffer(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _events = new Event[capacity];
    }

    public int Capacity => _events.Length;

    public int Count
    {
        get { lock (_lock) return _count; }
    }

    // Lifetime counter — never reset by Clear(). Consumers compute event rate
    // by sampling the delta over wall-clock time.
    public long TotalAppended
    {
        get { lock (_lock) return _totalAppended; }
    }

    public uint? LatestEventId
    {
        get
        {
            lock (_lock)
            {
                if (_count == 0)
                    return null;
                int last = (_writeIndex - 1 + _events.Length) % _events.Length;
                return _events[last].EventId;
            }
        }
    }

    public Event? PeekLatest()
    {
        lock (_lock)
        {
            if (_count == 0)
                return null;
            int last = (_writeIndex - 1 + _events.Length) % _events.Length;
            return _events[last];
        }
    }

    public void Append(Event evt)
    {
        lock (_lock)
        {
            _events[_writeIndex] = evt;
            _writeIndex = (_writeIndex + 1) % _events.Length;
            if (_count < _events.Length)
                _count++;
            _totalAppended++;
        }
    }

    public IReadOnlyList<Event> Snapshot()
    {
        lock (_lock)
        {
            if (_count == 0)
                return Array.Empty<Event>();

            var copy = new Event[_count];
            int start = (_writeIndex - _count + _events.Length) % _events.Length;
            for (int i = 0; i < _count; i++)
                copy[i] = _events[(start + i) % _events.Length];
            return copy;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            Array.Clear(_events, 0, _events.Length);
            _writeIndex = 0;
            _count = 0;
        }
    }
}
