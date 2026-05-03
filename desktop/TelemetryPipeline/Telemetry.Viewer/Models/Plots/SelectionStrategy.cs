using Telemetry.Core.Models;

namespace Telemetry.Viewer.Models.Plots;

// Encapsulates "which event parameter to extract from which channel."
// Hides Event/Channel/EventParameters traversal from settings and engines —
// callers just provide an Event and read the resulting double via TryExtract.
public sealed class SelectionStrategy
{
    public int ChannelId { get; }
    public ParamType Param { get; }

    public SelectionStrategy(int channelId, ParamType param)
    {
        ChannelId = channelId;
        Param = param;
    }

    public bool TryExtract(Event ev, out double value)
    {
        value = 0;
        if (ChannelId < 0 || ChannelId >= ev.Channels.Count) return false;

        var p = ev.Channels[ChannelId].Parameters;
        value = Param switch
        {
            ParamType.Area       => p.Area,
            ParamType.PeakHeight => p.PeakHeight,
            ParamType.PeakWidth  => p.PeakWidth,
            ParamType.Baseline   => p.Baseline,
            _                    => double.NaN,
        };
        return !double.IsNaN(value);
    }
}
