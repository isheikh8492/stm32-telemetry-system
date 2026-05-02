using System.IO.Ports;
using Telemetry.IO;

var availablePorts = SerialReader.GetAvailablePorts();
var selectedPort = args.Length > 0 ? args[0] : availablePorts.FirstOrDefault();

Console.WriteLine($"Available ports: {(availablePorts.Length == 0 ? "none" : string.Join(", ", availablePorts))}");

if (string.IsNullOrWhiteSpace(selectedPort))
{
    Console.WriteLine("No serial ports found.");
    return;
}

Console.WriteLine($"Opening {selectedPort} at {SerialReader.DefaultBaudRate} baud (raw byte dump, 5s)...");

using var port = new SerialPort(selectedPort, SerialReader.DefaultBaudRate)
{
    ReadTimeout = 200
};
port.Open();

var buffer = new byte[1024];
var deadline = DateTime.UtcNow.AddSeconds(5);
int totalBytes = 0;
int printedBytes = 0;
const int dumpCap = 256;

Console.WriteLine($"--- first {dumpCap} bytes (hex) ---");

while (DateTime.UtcNow < deadline)
{
    int n;
    try { n = port.Read(buffer, 0, buffer.Length); }
    catch (TimeoutException) { continue; }

    totalBytes += n;
    for (int i = 0; i < n && printedBytes < dumpCap; i++, printedBytes++)
    {
        Console.Write($"{buffer[i]:X2} ");
        if ((printedBytes + 1) % 16 == 0) Console.WriteLine();
    }
}

Console.WriteLine();
Console.WriteLine($"--- total bytes in 5s: {totalBytes} ({totalBytes / 5.0:0} B/s) ---");
