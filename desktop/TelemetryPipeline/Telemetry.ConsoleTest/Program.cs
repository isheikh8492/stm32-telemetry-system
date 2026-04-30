using Telemetry.IO;

var availablePorts = SerialReader.GetAvailablePorts();
var selectedPort = args.Length > 0 ? args[0] : availablePorts.FirstOrDefault();

Console.WriteLine($"Available ports: {(availablePorts.Length == 0 ? "none" : string.Join(", ", availablePorts))}");

if (string.IsNullOrWhiteSpace(selectedPort))
{
    Console.WriteLine("No serial ports found.");
    return;
}

Console.WriteLine($"Opening {selectedPort} at {SerialReader.DefaultBaudRate} baud...");

using var reader = new SerialReader(selectedPort, SerialReader.DefaultBaudRate);

reader.EventReceived += telemetryEvent =>
{
    Console.WriteLine(
    $"Event={telemetryEvent.EventId}, Time={telemetryEvent.TimestampMs}, Baseline={telemetryEvent.EventParameters.Baseline}, Area={telemetryEvent.EventParameters.Area}, PeakWidth={telemetryEvent.EventParameters.PeakWidth}, PeakHeight={telemetryEvent.EventParameters.PeakHeight}, Samples={telemetryEvent.Samples.Count}");
};

reader.ErrorOccurred += message =>
{
    Console.WriteLine(message);
};

Console.WriteLine("Listening...");
reader.Start();