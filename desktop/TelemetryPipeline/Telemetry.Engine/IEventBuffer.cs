using Telemetry.Core.Models;

namespace Telemetry.Engine;

// Tiny seam so Telemetry.Engine.BufferConsumer can append events to any
// downstream buffer without depending on Telemetry.Viewer's concrete types.
// RingBuffer and the viewer-side ChannelDataBuffer both implement it.
public interface IEventBuffer
{
    void Append(Event evt);
}
