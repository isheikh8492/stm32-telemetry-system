using System;
using System.Collections.Generic;
using System.Text;

namespace Telemetry.Core.Models
{
    public sealed record RawSample(
        uint PacketId,
        uint TimestampMs,
        int Ch1,
        int Ch2,
        int Ch3
    );
}
