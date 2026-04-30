using System.Globalization;
using Telemetry.Core.Models;

namespace Telemetry.Core
{
    public static class EventParser
    {
        private const int HeaderFieldCount = 8;

        public static Event? ParseLine(string line)
        {
            try
            {
                var parts = line.Trim().Split(',');
                if (parts.Length < HeaderFieldCount || !string.Equals(parts[0], "EVT", StringComparison.Ordinal))
                {
                    return null;
                }

                uint eventId = uint.Parse(parts[1], CultureInfo.InvariantCulture);
                uint timestampMs = uint.Parse(parts[2], CultureInfo.InvariantCulture);
                ushort baseline = ushort.Parse(parts[3], CultureInfo.InvariantCulture);
                uint area = uint.Parse(parts[4], CultureInfo.InvariantCulture);
                uint peakWidth = uint.Parse(parts[5], CultureInfo.InvariantCulture);
                ushort peakHeight = ushort.Parse(parts[6], CultureInfo.InvariantCulture);
                int sampleCount = int.Parse(parts[7], CultureInfo.InvariantCulture);

                if (sampleCount < 0 || parts.Length != HeaderFieldCount + sampleCount)
                {
                    return null;
                }

                var samples = new ushort[sampleCount];
                for (int i = 0; i < sampleCount; i++)
                {
                    samples[i] = ushort.Parse(parts[HeaderFieldCount + i], CultureInfo.InvariantCulture);
                }

                return new Event(
                    eventId,
                    timestampMs,
                    samples,
                    new EventParameters(baseline, area, peakWidth, peakHeight));
            }
            catch
            {
                return null;
            }
        }
    }
}