using System.Windows.Media;
using Telemetry.Core.Models;
using Telemetry.Viewer.Services.Channels;

namespace Telemetry.Viewer.Models.Plots;

// Encapsulates "which event parameter to extract from which channel."
// Hides Event/Channel/EventParameters traversal from settings and engines —
// callers just provide an Event and read the resulting double via TryExtract.
//
// Also the single funnel for channel-related lookups everything else needs:
//   * Plot views read Label / Color via the resolved Channel.
//   * Dialog ComboBoxes populate from AvailableChannels / AvailableParams
//     instead of touching ChannelCatalog directly.
public sealed class SelectionStrategy
{
    public int ChannelId { get; }
    public ParamType Param { get; }

    public SelectionStrategy(int channelId, ParamType param)
    {
        ChannelId = channelId;
        Param = param;
    }

    // ---- Resolved metadata for THIS selection ----

    // Looks the channel up in the catalog at point-of-use; the catalog's
    // descriptor is the source of truth for name/color, and renames/recolors
    // surface here automatically.
    public ChannelDescriptor Channel =>
        ChannelId >= 0 && ChannelId < ChannelCatalog.Count
            ? ChannelCatalog.Get(ChannelId)
            : Fallback(ChannelId);

    // Pre-formatted "Channel, Param" string — used by axis labels.
    public string Label => $"{Channel.Name}, {Param}";

    // Channel's display color — used by trace painting in plot processors.
    public Color Color => Channel.Color;

    // ---- Available options for dialog ComboBoxes ----

    public static IReadOnlyList<ChannelDescriptor> AvailableChannels => ChannelCatalog.All;
    public static IReadOnlyList<ParamType> AvailableParams { get; } = Enum.GetValues<ParamType>();

    // Resolve a raw channel id to its descriptor — single funnel used by
    // multi-channel plots (Oscilloscope) that need name/color but don't
    // carry a SelectionStrategy instance per channel.
    public static ChannelDescriptor GetChannel(int id)
        => id >= 0 && id < ChannelCatalog.Count ? ChannelCatalog.Get(id) : Fallback(id);

    // Multi-select dialog helpers. Dialogs deal in object refs from
    // ListBox.SelectedItems and don't need to know the underlying type;
    // these convert between channel-id lists and the descriptor objects
    // the ListBox is bound to.
    public static IEnumerable<object> ChannelsForIds(IEnumerable<int> ids)
        => ids.Where(id => id >= 0 && id < ChannelCatalog.Count)
              .Select(id => (object)ChannelCatalog.Get(id));

    public static IReadOnlyList<int> IdsFromSelectedItems(System.Collections.IList items)
    {
        var ids = new List<int>(items.Count);
        foreach (var o in items)
            if (o is ChannelDescriptor d) ids.Add(d.Id);
        ids.Sort();
        return ids;
    }

    // ComboBox binding paths for AvailableChannels — keeps the descriptor's
    // member names out of dialog code so SelectionStrategy is the only place
    // that knows about ChannelDescriptor.
    public static string ChannelDisplayPath { get; } = nameof(ChannelDescriptor.Name);
    public static string ChannelValuePath   { get; } = nameof(ChannelDescriptor.Id);

    // ---- Extraction (worker-thread) ----

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

    // Out-of-range or empty-catalog fallback so callers never see a null.
    private static ChannelDescriptor Fallback(int id)
        => new(id, $"Ch {id}", Colors.Gray);
}
