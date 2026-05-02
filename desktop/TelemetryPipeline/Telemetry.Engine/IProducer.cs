using System.Threading.Channels;
using Telemetry.Core.Models;

namespace Telemetry.Engine;

public interface IProducer
{
    ChannelReader<Event> Reader { get; }
    void Start();
    void Stop();
}
