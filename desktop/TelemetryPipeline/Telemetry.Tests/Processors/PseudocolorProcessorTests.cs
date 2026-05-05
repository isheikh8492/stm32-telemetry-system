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

// A/B harness for the 2D heatmap. Same shape as HistogramProcessorTests:
//   1. correctness — production = naive on full capacity AND after ring wrap
//   2. performance — production is meaningfully faster at steady state
public sealed class PseudocolorProcessorTests
{
    private const int Capacity = 10_000;
    private const int Channels = 60;
    private const int PixelW   = 240;
    private const int PixelH   = 240;

    private readonly ITestOutputHelper _out;
    public PseudocolorProcessorTests(ITestOutputHelper output) { _out = output; }

    private static PseudocolorSettings MakeSettings() => new(
        plotId:    Guid.NewGuid(),
        xChannelId: 0, xParam: ParamType.PeakHeight,
        yChannelId: 1, yParam: ParamType.Area,
        binCount:   BinCount.Bins256,
        xMinRange:  1, xMaxRange: 1_000_000,
        yMinRange:  1, yMaxRange: 1_000_000,
        xScale:     AxisScale.Logarithmic, yScale: AxisScale.Logarithmic);

    private static IEnumerable<Event> Stream(int count, Random rng)
    {
        for (uint i = 0; i < count; i++)
        {
            yield return EventBuilder.Build((uint)(i + 1), Channels, c =>
            {
                var ph   = (ushort)Math.Clamp((int)Math.Pow(10, 1 + 4 * rng.NextDouble()), 1, 65535);
                var area = (uint)  Math.Clamp((int)Math.Pow(10, 1 + 4 * rng.NextDouble()), 1, int.MaxValue);
                return new EventParameters(Baseline: 1500, Area: area, PeakWidth: 0, PeakHeight: ph);
            });
        }
    }

    [Fact]
    public void Incremental_MatchesNaive_AfterRingWrap()
    {
        var (buffer, source) = BufferFactory.Make(Capacity, Channels);
        BufferFactory.FillWith(buffer, Stream(Capacity * 5 / 2, new Random(11)));

        var settings = MakeSettings();
        var inc      = new PseudocolorPlotProcessor();
        var naive    = new NaivePseudocolorProcessor();

        for (int round = 0; round < 3; round++)
            inc.Process(settings, source, PixelW, PixelH);

        var fInc   = (PseudocolorFrame)inc  .Process(settings, source, PixelW, PixelH)!;
        var fNaive = (PseudocolorFrame)naive.Process(settings, source, PixelW, PixelH)!;

        Assert.Equal(fNaive.PixelWidth, fInc.PixelWidth);
        Assert.Equal(fNaive.PixelHeight, fInc.PixelHeight);
        Assert.Equal(fNaive.Buffer, fInc.Buffer);
    }

    [Fact]
    public void Incremental_IsFasterThanNaive_AtSteadyState()
    {
        var (buffer, source) = BufferFactory.Make(Capacity, Channels);
        BufferFactory.FillWith(buffer, Stream(Capacity, new Random(1)));

        var settings = MakeSettings();
        var inc      = new PseudocolorPlotProcessor();
        var naive    = new NaivePseudocolorProcessor();

        for (int i = 0; i < 5; i++)
        {
            inc  .Process(settings, source, PixelW, PixelH);
            naive.Process(settings, source, PixelW, PixelH);
        }

        const int Ticks = 30;
        var rng = new Random(71);

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

        _out.WriteLine($"Pseudocolor incremental: {incMs:F4} ms/tick");
        _out.WriteLine($"Pseudocolor naive      : {naiveMs:F4} ms/tick");
        _out.WriteLine($"Pseudocolor speedup    : {ratio:F1}×");

        Assert.True(ratio >= 2,
            $"Expected incremental ≥2× faster, got {ratio:F1}× ({incMs:F4} vs {naiveMs:F4} ms)");
    }
}
