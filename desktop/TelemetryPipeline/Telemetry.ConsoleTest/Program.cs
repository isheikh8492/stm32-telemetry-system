using Telemetry.IO;

using var reader = new SerialReader("COM4", 115200);

reader.SampleReceived += sample =>
{
    Console.WriteLine(
        $"Packet={sample.PacketId}, Time={sample.TimestampMs}, Ch1={sample.Ch1}, Ch2={sample.Ch2}, Ch3={sample.Ch3}");
};

reader.ErrorOccurred += line =>
{
    Console.WriteLine($"Bad: {line}");
};

Console.WriteLine("Listening...");
reader.Start();