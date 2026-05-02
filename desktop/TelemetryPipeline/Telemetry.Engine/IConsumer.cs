namespace Telemetry.Engine;

public interface IConsumer
{
    Task RunAsync(CancellationToken token);
}
