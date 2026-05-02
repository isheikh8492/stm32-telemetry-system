using System.Buffers.Binary;
using System.IO.Ports;
using System.Linq;
using Telemetry.Core.Models;

namespace Telemetry.IO
{
    public sealed class SerialReader : IDisposable
    {
        public const int DefaultBaudRate = 2000000;

        // Wire format:
        //   [0xA5][0x5A][event_id u32 LE][timestamp_ms u32 LE][channel_count u16 LE][sample_count u16 LE]
        //   [samples u16[C*N] LE]   (channel-major)
        //   [params per channel: baseline u16, area u32, peakWidth u32, peakHeight u16]   // 12 bytes/channel
        private const byte Sync1 = 0xA5;
        private const byte Sync2 = 0x5A;
        private const int HeaderBytes = 12;
        private const int ParamBytesPerChannel = 12;
        private const int MaxChannelCount = 256;
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
                        ushort channelCount = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(8, 2));
                        ushort sampleCount = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(10, 2));

                        if (channelCount == 0 || channelCount > MaxChannelCount) continue;
                        if (sampleCount == 0 || sampleCount > MaxSampleCount) continue;

                        int sampleBlockBytes = channelCount * sampleCount * 2;
                        int paramBlockBytes = channelCount * ParamBytesPerChannel;

                        var sampleBuffer = new byte[sampleBlockBytes];
                        ReadExact(sampleBuffer, sampleBlockBytes);

                        var paramBuffer = new byte[paramBlockBytes];
                        ReadExact(paramBuffer, paramBlockBytes);

                        var channels = new Channel[channelCount];
                        for (int c = 0; c < channelCount; c++)
                        {
                            var samples = new ushort[sampleCount];
                            Buffer.BlockCopy(sampleBuffer, c * sampleCount * 2, samples, 0, sampleCount * 2);

                            int pOff = c * ParamBytesPerChannel;
                            ushort baseline = BinaryPrimitives.ReadUInt16LittleEndian(paramBuffer.AsSpan(pOff, 2));
                            uint area = BinaryPrimitives.ReadUInt32LittleEndian(paramBuffer.AsSpan(pOff + 2, 4));
                            uint peakWidth = BinaryPrimitives.ReadUInt32LittleEndian(paramBuffer.AsSpan(pOff + 6, 4));
                            ushort peakHeight = BinaryPrimitives.ReadUInt16LittleEndian(paramBuffer.AsSpan(pOff + 10, 2));

                            channels[c] = new Channel(c, samples, new EventParameters(baseline, area, peakWidth, peakHeight));
                        }

                        EventReceived?.Invoke(new Event(eventId, timestampMs, channels));
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
