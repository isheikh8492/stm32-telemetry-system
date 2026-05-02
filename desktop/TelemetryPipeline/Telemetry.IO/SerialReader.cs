using System.Buffers.Binary;
using System.IO.Ports;
using System.Linq;
using Telemetry.Core.Models;

namespace Telemetry.IO
{
    public sealed class SerialReader : IDisposable
    {
        public const int DefaultBaudRate = 115200;

        // Wire format:
        //   [0xA5][0x5A][event_id u32 LE][timestamp_ms u32 LE][sample_count u16 LE][samples u16[N] LE]
        private const byte Sync1 = 0xA5;
        private const byte Sync2 = 0x5A;
        private const int HeaderBytes = 10;
        private const int MaxSampleCount = 4096;

        private readonly SerialPort _serialPort;
        private bool _running;

        public event Action<Event>? EventReceived;
        public event Action<string>? ErrorOccurred;

        public static string[] GetAvailablePorts()
        {
            return SerialPort.GetPortNames()
                .OrderBy(static port => port)
                .ToArray();
        }

        public SerialReader(string portName, int baudRate)
        {
            _serialPort = new SerialPort(portName, baudRate)
            {
                ReadTimeout = 1000
            };
        }

        public void Start()
        {
            try
            {
                _serialPort.Open();
                _running = true;

                var header = new byte[HeaderBytes];

                while (_running)
                {
                    try
                    {
                        if (_serialPort.ReadByte() != Sync1) continue;
                        if (_serialPort.ReadByte() != Sync2) continue;

                        ReadExact(header, HeaderBytes);
                        uint eventId = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));
                        uint timestampMs = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4));
                        ushort sampleCount = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(8, 2));

                        if (sampleCount == 0 || sampleCount > MaxSampleCount)
                            continue;

                        var sampleBytes = new byte[sampleCount * 2];
                        ReadExact(sampleBytes, sampleBytes.Length);

                        var samples = new ushort[sampleCount];
                        Buffer.BlockCopy(sampleBytes, 0, samples, 0, sampleBytes.Length);

                        EventReceived?.Invoke(new Event(eventId, timestampMs, samples));
                    }
                    catch (TimeoutException)
                    {
                        // Partial frame — drop back to hunting; next 0xA5 0x5A re-syncs.
                    }
                    catch (Exception ex)
                    {
                        if (_running)
                            ErrorOccurred?.Invoke($"Error reading from serial port: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"Error opening serial port: {ex.Message}");
            }
        }

        private void ReadExact(byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n = _serialPort.Read(buffer, total, count - total);
                if (n == 0) throw new IOException("Serial port closed.");
                total += n;
            }
        }

        public void Stop()
        {
            _running = false;
            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
            }
        }

        public void Dispose()
        {
            Stop();
            _serialPort.Dispose();
        }
    }
}
