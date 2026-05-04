namespace Telemetry.Viewer.Models.Plots;

// Oscilloscope: latest event's waveform per channel. Can show multiple
// channels at once — each painted in its own ChannelDescriptor.Color.
public sealed class OscilloscopeSettings : PlotSettings
{
    private IReadOnlyList<int> _channelIds;

    public OscilloscopeSettings(Guid plotId, IReadOnlyList<int> channelIds) : base(plotId)
    {
        _channelIds = channelIds;
    }

    public IReadOnlyList<int> ChannelIds
    {
        get => _channelIds;
        set
        {
            if (SetProperty(ref _channelIds, value))
                OnPropertyChanged(nameof(DisplayName));
        }
    }

    public override PlotType Type => PlotType.Oscilloscope;
    public override string DisplayName =>
        _channelIds.Count switch
        {
            0 => "Oscilloscope",
            1 => $"Oscilloscope (ch {_channelIds[0]})",
            _ => $"Oscilloscope ({_channelIds.Count} ch)"
        };
}
