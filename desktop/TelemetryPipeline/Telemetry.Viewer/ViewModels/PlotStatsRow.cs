using Telemetry.Viewer.Common;
using Telemetry.Viewer.Models.Plots;

namespace Telemetry.Viewer.ViewModels;

// One row in the per-plot-type processing-stats list. Each (PlotType, Kind)
// pair gets its own row, so e.g. Oscilloscope produces TWO rows in the
// sidebar — "Oscilloscope (Processing)" and "Oscilloscope (Rendering)".
//
// Updated in place every stats tick — keeping the same instance avoids
// ObservableCollection reset/insert churn.
public sealed class PlotStatsRow : ObservableObject
{
    public PlotType Type { get; }
    public bool IsProcessing { get; }
    public string Name { get; }

    private string _value = "—";
    public string Value { get => _value; set => SetProperty(ref _value, value); }

    public PlotStatsRow(PlotType type, bool isProcessing)
    {
        Type = type;
        IsProcessing = isProcessing;
        Name = $"{type} ({(isProcessing ? "Processing" : "Rendering")})";
    }
}
