namespace Telemetry.IO;

// Owns serial-port hardware metadata (available ports, valid baud rates,
// the default). Wraps the static SerialReader surface so callers don't
// import the Telemetry.IO namespace just for these lookups.
public interface IPortDiscovery
{
    IReadOnlyList<string> GetAvailablePorts();
    int DefaultBaudRate { get; }
    IReadOnlyList<int> SupportedBaudRates { get; }
}

public sealed class SerialPortDiscovery : IPortDiscovery
{
    private static readonly int[] _supportedBaudRates =
        { 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600, 2000000 };

    public IReadOnlyList<string> GetAvailablePorts() => SerialReader.GetAvailablePorts();
    public int DefaultBaudRate => SerialReader.DefaultBaudRate;
    public IReadOnlyList<int> SupportedBaudRates => _supportedBaudRates;
}
