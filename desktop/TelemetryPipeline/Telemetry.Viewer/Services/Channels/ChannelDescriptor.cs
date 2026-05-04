using System.Windows.Media;
using Telemetry.Viewer.Common;

namespace Telemetry.Viewer.Services.Channels;

// Per-channel metadata read across the app:
//   * Plot views — axis labels (e.g. histogram X-label "ADC0, PeakHeight")
//   * Plot processors — trace / bar color in painted bitmaps
//   * Pseudocolor / spectral-ribbon plots — channel ordering + naming
//   * SelectionStrategy — looks up channel by id at point of use
//
// Mutable + ObservableObject: renaming a channel or recoloring it fires
// PropertyChanged so subscribed views re-render. Id is set once at
// construction (it's the position in the catalog).
public sealed class ChannelDescriptor : ObservableObject
{
    public int Id { get; }

    private string _name;
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    private Color _color;
    public Color Color
    {
        get => _color;
        set => SetProperty(ref _color, value);
    }

    public ChannelDescriptor(int id, string name, Color color)
    {
        Id = id;
        _name = name;
        _color = color;
    }
}
