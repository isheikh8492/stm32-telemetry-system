using System.IO.Ports;
using System.Linq;
using Telemetry.Core;
using Telemetry.Core.Models;

namespace Telemetry.IO
{
    public sealed class SerialReader : IDisposable
    {
        public const int DefaultBaudRate = 115200;

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
                NewLine = "\n",
                ReadTimeout = 1000
            };
        }

        public void Start()
        {
            try
            {
                _serialPort.Open();
                _running = true;
                while (_running)
                {
                    try
                    {
                        var line = _serialPort.ReadLine();
                        var telemetryEvent = EventParser.ParseLine(line);
                        if (telemetryEvent != null)
                        {
                            EventReceived?.Invoke(telemetryEvent);
                        }
                    }
                    catch (TimeoutException)
                    {
                        // Ignore timeout exceptions, just continue reading
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
