using System.Diagnostics;
using Telemetry.Core.Models;
using Telemetry.Tests.Helpers;
using Telemetry.Tests.Reference;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.Pipeline.Processors;
using Xunit;
using Xunit.Abstractions;

namespace Telemetry.Tests.Processors;

// Cross-validates the production incremental HistogramPlotProcessor against
// the naive full-snapshot reference. Each test asserts both:
//   1. correctness — same YMax + same painted byte[] given the same inputs
//   2. performance — incremental Process is meaningfully faster than naive,
//      so the architectural decision to keep per-plot ring/bin FIFOs holds
//      up under measurement, not just intuition.
public sealed class HistogramProcessorTests
{
    private const int Capacity   = 10_000;
    private const int Channels   = 60;
    private const int PixelW     = 200;
    private const int PixelH     = 120;

    private readonly ITestOutputHelper _out;
    public HistogramProcessorTests(ITestOutputHelper output) { _out = output; }

    private static HistogramSettings MakeSettings() => new(
        plotId:    Guid.NewGuid(),
        channelId: 0,
        param:     ParamType.PeakHeight,
        binCount:  BinCount.Bins256,
        minRange:  1, maxRange: 1_000_000,
        scale:     AxisScale.Logarithmic);

    private static IEnumerable<Event> Stream(int count, Random rng)
    {
        for (uint i = 0; i < count; i++)
        {
            // Spread peakHeight across the log range so most bins get hit.
            yield return EventBuilder.Build((uint)(i + 1), Channels, c =>
            {
                var ph = (ushort)Math.Clamp((int)Math.Pow(10, 1 + 4 * rng.NextDouble()), 1, 65535);
                return new EventParameters(Baseline: 1500, Area: 0, PeakWidth: 0, PeakHeight: ph);
            });
        }
    }

    [Fact]
    public void Incremental_MatchesNaive_OnFullCapacity()
    {
        var (buffer, source) = BufferFactory.Make(Capacity, Channels);
        BufferFactory.FillWith(buffer, Stream(Capacity, new Random(42)));

        var settings = MakeSettings();
        var inc      = new HistogramPlotProcessor();
        var naive    = new NaiveHistogramProcessor();

        var fInc   = (HistogramFrame)inc  .Process(settings, source, PixelW, PixelH)!;
        var fNaive = (HistogramFrame)naive.Process(settings, source, PixelW, PixelH)!;

        Assert.Equal(fNaive.YMax, fInc.YMax);
        Assert.Equal(fNaive.PixelWidth, fInc.PixelWidth);
        Assert.Equal(fNaive.PixelHeight, fInc.PixelHeight);
        Assert.Equal(fNaive.Buffer, fInc.Buffer);
    }

    [Fact]
    public void Incremental_MatchesNaive_AfterRingWrap()
    {
        // Push 2.5× capacity so the ring wraps and the FIFO has had to
        // evict heads — exercises the eviction path, not just the append path.
        var (buffer, source) = BufferFactory.Make(Capacity, Channels);
        BufferFactory.FillWith(buffer, Stream(Capacity * 5 / 2, new Random(7)));

        var settings = MakeSettings();
        var inc      = new HistogramPlotProcessor();
        var naive    = new NaiveHistogramProcessor();

        // Drive the incremental processor through several intermediate states
        // so its LastSequence cursor is non-trivial when we compare.
        for (int round = 0; round < 3; round++)
            inc.Process(settings, source, PixelW, PixelH);

        var fInc   = (HistogramFrame)inc  .Process(settings, source, PixelW, PixelH)!;
        var fNaive = (HistogramFrame)naive.Process(settings, source, PixelW, PixelH)!;

        Assert.Equal(fNaive.YMax, fInc.YMax);
        Assert.Equal(fNaive.Buffer, fInc.Buffer);
    }

    [Fact]
    public void Incremental_IsFasterThanNaive_AtSteadyState()
    {
        // Steady state: buffer is already full, then 1 new event arrives per tick
        // — the production scenario. Incremental is O(1); naive is O(Capacity).
        var (buffer, source) = BufferFactory.Make(Capacity, Channels);
        BufferFactory.FillWith(buffer, Stream(Capacity, new Random(1)));

        var settings = MakeSettings();
        var inc      = new HistogramPlotProcessor();
        var naive    = new NaiveHistogramProcessor();

        // Warm up both — JIT, cache fill, etc.
        for (int i = 0; i < 5; i++)
        {
            inc  .Process(settings, source, PixelW, PixelH);
            naive.Process(settings, source, PixelW, PixelH);
        }

        // Time N steady-state ticks each, with one new event between ticks
        // to reflect production behavior.
        const int Ticks = 50;
        var rng = new Random(99);

        var swInc = Stopwatch.StartNew();
        for (int i = 0; i < Ticks; i++)
        {
            buffer.Append(Stream(1, rng).First());
            inc.Process(settings, source, PixelW, PixelH);
        }
        swInc.Stop();

        var swNaive = Stopwatch.StartNew();
        for (int i = 0; i < Ticks; i++)
        {
            buffer.Append(Stream(1, rng).First());
            naive.Process(settings, source, PixelW, PixelH);
        }
        swNaive.Stop();

        var incMs   = swInc.Elapsed.TotalMilliseconds   / Ticks;
        var naiveMs = swNaive.Elapsed.TotalMilliseconds / Ticks;
        var ratio   = naiveMs / Math.Max(incMs, 1e-6);

        _out.WriteLine($"Histogram   incremental: {incMs:F4} ms/tick");
        _out.WriteLine($"Histogram   naive      : {naiveMs:F4} ms/tick");
        _out.WriteLine($"Histogram   speedup    : {ratio:F1}×");

        Assert.True(ratio >= 2,
            $"Expected incremental ≥2× faster, got {ratio:F1}× ({incMs:F4} vs {naiveMs:F4} ms)");
    }
}
