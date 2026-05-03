namespace Telemetry.Viewer.Models.Plots;

public sealed class OscilloscopeSettings : PlotSettings
{
    private int _channelId;

    public OscilloscopeSettings(Guid plotId, int channelId) : base(plotId)
    {
        _channelId = channelId;
    }

    public int ChannelId
    {
        get => _channelId;
        set
        {
            if (SetProperty(ref _channelId, value))
                OnPropertyChanged(nameof(DisplayName));
        }
    }

    public override string DisplayName => $"Oscilloscope (ch {_channelId})";
}
