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

// A/B for the 60-channel spectral ribbon — the heaviest analysis processor
// (per-tick work scales with channelCount × events). The naive recompute
// loops the snapshot × channelCount, so the speedup ratio is the most
// dramatic of the three plot types.
public sealed class SpectralRibbonProcessorTests
{
    private const int Capacity = 10_000;
    private const int Channels = 60;
    private const int PixelW   = 1040;
    private const int PixelH   = 160;

    private readonly ITestOutputHelper _out;
    public SpectralRibbonProcessorTests(ITestOutputHelper output) { _out = output; }

    private static SpectralRibbonSettings MakeSettings() => new(
        plotId:     Guid.NewGuid(),
        channelIds: Enumerable.Range(0, Channels).ToArray(),
        param:      ParamType.PeakHeight,
        binCount:   BinCount.Bins256,
        minRange:   1, maxRange: 1_000_000,
        scale:      AxisScale.Logarithmic);

    private static IEnumerable<Event> Stream(int count, Random rng)
    {
        for (uint i = 0; i < count; i++)
        {
            yield return EventBuilder.Build((uint)(i + 1), Channels, c =>
            {
                var ph = (ushort)Math.Clamp((int)Math.Pow(10, 1 + 4 * rng.NextDouble()), 1, 65535);
                return new EventParameters(Baseline: 1500, Area: 0, PeakWidth: 0, PeakHeight: ph);
            });
        }
    }

    [Fact]
    public void Incremental_MatchesNaive_AfterRingWrap()
    {
        var (buffer, source) = BufferFactory.Make(Capacity, Channels);
        BufferFactory.FillWith(buffer, Stream(Capacity * 5 / 2, new Random(23)));

        var settings = MakeSettings();
        var inc      = new SpectralRibbonPlotProcessor();
        var naive    = new NaiveSpectralRibbonProcessor();

        for (int round = 0; round < 3; round++)
            inc.Process(settings, source, PixelW, PixelH);

        var fInc   = (SpectralRibbonFrame)inc  .Process(settings, source, PixelW, PixelH)!;
        var fNaive = (SpectralRibbonFrame)naive.Process(settings, source, PixelW, PixelH)!;

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
        var inc      = new SpectralRibbonPlotProcessor();
        var naive    = new NaiveSpectralRibbonProcessor();

        for (int i = 0; i < 5; i++)
        {
            inc  .Process(settings, source, PixelW, PixelH);
            naive.Process(settings, source, PixelW, PixelH);
        }

        const int Ticks = 20;
        var rng = new Random(13);

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

        _out.WriteLine($"SpectralRibbon incremental: {incMs:F4} ms/tick");
        _out.WriteLine($"SpectralRibbon naive      : {naiveMs:F4} ms/tick");
        _out.WriteLine($"SpectralRibbon speedup    : {ratio:F1}×");

        Assert.True(ratio >= 2,
            $"Expected incremental ≥2× faster, got {ratio:F1}× ({incMs:F4} vs {naiveMs:F4} ms)");
    }
}
