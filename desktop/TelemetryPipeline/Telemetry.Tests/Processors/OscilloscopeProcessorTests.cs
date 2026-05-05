using System.Diagnostics;
using Telemetry.Core.Models;
using Telemetry.Tests.Helpers;
using Telemetry.Viewer.Models.Plots;
using Telemetry.Viewer.Services.Pipeline.Processors;
using Xunit;
using Xunit.Abstractions;

namespace Telemetry.Tests.Processors;

// Oscilloscope is stateless and O(samples × selectedChannels) per tick — there
// is no naive-vs-incremental story because there's no across-event accumulation.
// Instead we measure absolute throughput at production-relevant channel counts
// (1, 16, 60) so we can quote concrete ms/tick numbers for the highest-rate
// plot in the pipeline (it runs at 30 Hz versus analytics' 4 Hz).
public sealed class OscilloscopeProcessorTests
{
    private const int Capacity = 10_000;
    private const int Channels = 60;
    private const int Samples  = 32;
    private const int PixelW   = 280;
    private const int PixelH   = 160;

    private readonly ITestOutputHelper _out;
    public OscilloscopeProcessorTests(ITestOutputHelper output) { _out = output; }

    private static Event MakeEvent(uint id, Random rng)
    {
        var samples = new ushort[Samples];
        for (int i = 0; i < Samples; i++) samples[i] = (ushort)rng.Next(0, 4096);
        var channels = new Channel[Channels];
        for (int c = 0; c < Channels; c++)
            channels[c] = new Channel(c, samples, new EventParameters(0, 0, 0, 0));
        return new Event(id, id, channels);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(60)]
    public void Throughput_AtSelectedChannelCount(int selectedChannels)
    {
        var (buffer, source) = BufferFactory.Make(Capacity, Channels);
        var rng = new Random(42);
        // Only the latest event is read by the processor — but append a few so
        // PeekLatest returns something non-null and we exercise a real frame.
        for (uint i = 0; i < 5; i++) buffer.Append(MakeEvent(i + 1, rng));

        var settings = new OscilloscopeSettings(
            plotId: Guid.NewGuid(),
            channelIds: Enumerable.Range(0, selectedChannels).ToArray());
        var processor = new OscilloscopePlotProcessor();

        // Warm up
        for (int i = 0; i < 5; i++)
            processor.Process(settings, source, PixelW, PixelH);

        const int Ticks = 200;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < Ticks; i++)
        {
            buffer.Append(MakeEvent((uint)(100 + i), rng));
            processor.Process(settings, source, PixelW, PixelH);
        }
        sw.Stop();

        var perTick = sw.Elapsed.TotalMilliseconds / Ticks;
        _out.WriteLine($"Oscilloscope ({selectedChannels} ch × {Samples} samples): {perTick:F4} ms/tick");

        // 30 Hz cadence = 33 ms budget per tick. Even at 60 channels we're
        // expected to be well under that — assert a generous 5 ms ceiling so
        // the test catches regressions without flaking on slow hardware.
        Assert.True(perTick < 5,
            $"Oscilloscope @ {selectedChannels} ch took {perTick:F4} ms/tick (budget: 5 ms)");
    }
}
