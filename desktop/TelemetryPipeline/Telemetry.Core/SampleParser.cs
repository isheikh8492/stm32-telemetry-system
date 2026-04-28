using System.Globalization;
using Telemetry.Core.Models;

namespace Telemetry.Core
{
    public static class SampleParser
    {
        public static RawSample? ParseLine(string line)
        {
            try
            {
                var parts = line.Trim().Split(',');
                if (parts.Length != 5)
                {
                    return null; // Invalid format
                }
                uint packetId = uint.Parse(parts[0], CultureInfo.InvariantCulture);
                uint timestampMs = uint.Parse(parts[1], CultureInfo.InvariantCulture);
                int ch1 = int.Parse(parts[2], CultureInfo.InvariantCulture);
                int ch2 = int.Parse(parts[3], CultureInfo.InvariantCulture);
                int ch3 = int.Parse(parts[4], CultureInfo.InvariantCulture);
                return new RawSample(packetId, timestampMs, ch1, ch2, ch3);
            }
            catch
            {
                return null; // Parsing failed
            }
        }
    }
}
