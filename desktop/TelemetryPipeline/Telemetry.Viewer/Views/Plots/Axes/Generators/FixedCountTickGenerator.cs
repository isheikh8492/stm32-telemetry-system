using ScottPlot;

namespace Telemetry.Viewer.Views.Plots.Axes;

// Always emits a fixed number of major ticks evenly spaced across the
// current axis range. ScottPlot calls Regenerate with the live range every
// time the axis bounds change, so labels update automatically while the
// tick LAYOUT (positions in axis space) stays constant. Plot views can
// install this once in their static layer and stop touching ticks per
// render — only SetLimitsY (or SetLimitsX) is needed to drive the labels.
public sealed class FixedCountTickGenerator : ITickGenerator
{
    private readonly int _majorCount;
    private readonly Func<double, string> _format;
    private Tick[] _ticks = Array.Empty<Tick>();

    public FixedCountTickGenerator(int majorCount, Func<double, string> format)
    {
        if (majorCount < 2) throw new ArgumentOutOfRangeException(nameof(majorCount));
        _majorCount = majorCount;
        _format = format;
    }

    public Tick[] Ticks => _ticks;
    public int MaxTickCount { get; set; } = int.MaxValue;

    public void Regenerate(CoordinateRange range, Edge edge, PixelLength length, Paint paint, LabelStyle labelStyle)
    {
        var ticks = new Tick[_majorCount];
        var span = range.Max - range.Min;
        for (int i = 0; i < _majorCount; i++)
        {
            var t = (double)i / (_majorCount - 1);
            var value = range.Min + t * span;
            ticks[i] = new Tick(value, _format(value), true);
        }
        _ticks = ticks;
    }
}
