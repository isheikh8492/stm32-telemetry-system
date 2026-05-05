using Telemetry.Core.Models;
using Telemetry.Tests.Helpers;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Services.DataSources;
using Xunit;

namespace Telemetry.Tests;

// Pins down the contract of ChannelDataBuffer — the columnar feature ring
// that replaced the original RingBuffer<Event>. These tests are why we can
// confidently run 240 plots reading the same buffer without race-induced
// distribution corruption.
public sealed class ChannelDataBufferTests
{
    private const int Capacity = 8;
    private const int Channels = 4;

    [Fact]
    public void Append_StoresParamsByChannelId_NotByListPosition()
    {
        // Send an event with channels in REVERSE order — channel id is the
        // truth, not the list index. This is the bug class we hit and pinned.
        var buffer = new ChannelDataBuffer(Capacity, Channels);
        var reversed = new[]
        {
            new Channel(3, Array.Empty<ushort>(), new EventParameters(0, 30, 0, 300)),
            new Channel(2, Array.Empty<ushort>(), new EventParameters(0, 20, 0, 200)),
            new Channel(1, Array.Empty<ushort>(), new EventParameters(0, 10, 0, 100)),
            new Channel(0, Array.Empty<ushort>(), new EventParameters(0,  0, 0,   0)),
        };
        buffer.Append(new Event(EventId: 1, TimestampMs: 0, Channels: reversed));

        for (int c = 0; c < Channels; c++)
        {
            var fi = ChannelDataBuffer.FeatureIndex(c, ParamType.PeakHeight);
            var snap = buffer.GetSnapshot(fi);
            Assert.Equal(c * 100, snap.At(0));
        }
    }

    [Fact]
    public void Clear_ResetsCountTotalAppendedAndLatest()
    {
        var buffer = new ChannelDataBuffer(Capacity, Channels);
        for (uint i = 0; i < 5; i++)
            buffer.Append(EventBuilder.UniformPeakHeight(i + 1, Channels, peakHeight: (ushort)(i + 1)));

        Assert.Equal(5, buffer.Count);
        Assert.Equal(5, buffer.TotalAppended);
        Assert.NotNull(buffer.PeekLatest());

        buffer.Clear();

        Assert.Equal(0, buffer.Count);
        Assert.Equal(0, buffer.TotalAppended);
        Assert.Null(buffer.PeekLatest());
        Assert.Null(buffer.LatestEventId);
    }

    [Fact]
    public void GetSnapshot_AfterWrap_ExposesOnlyTheTrailingWindow()
    {
        var buffer = new ChannelDataBuffer(Capacity, Channels);
        // Push 2× capacity so the oldest events are evicted.
        for (uint i = 0; i < Capacity * 2; i++)
            buffer.Append(EventBuilder.UniformPeakHeight(i + 1, Channels, peakHeight: (ushort)(i + 1)));

        var snap = buffer.GetSnapshot(ChannelDataBuffer.FeatureIndex(0, ParamType.PeakHeight));
        Assert.Equal(Capacity, snap.Count);
        Assert.Equal(Capacity * 2, snap.EndSequence);
        Assert.Equal(Capacity, snap.EndSequence - snap.StartSequence);
        // Most recent event's value should be at the latest sequence slot.
        Assert.Equal(Capacity * 2, (int)snap.At(snap.EndSequence - 1));
    }

    [Fact]
    public void MissingChannel_LeavesSlotAsNaN()
    {
        var buffer = new ChannelDataBuffer(Capacity, Channels);
        // Channels 0 and 2 only — 1 and 3 should land as NaN.
        buffer.Append(new Event(EventId: 1, TimestampMs: 0, Channels: new[]
        {
            new Channel(0, Array.Empty<ushort>(), new EventParameters(0, 0, 0, 100)),
            new Channel(2, Array.Empty<ushort>(), new EventParameters(0, 0, 0, 200)),
        }));

        var s0 = buffer.GetSnapshot(ChannelDataBuffer.FeatureIndex(0, ParamType.PeakHeight));
        var s1 = buffer.GetSnapshot(ChannelDataBuffer.FeatureIndex(1, ParamType.PeakHeight));
        var s2 = buffer.GetSnapshot(ChannelDataBuffer.FeatureIndex(2, ParamType.PeakHeight));
        var s3 = buffer.GetSnapshot(ChannelDataBuffer.FeatureIndex(3, ParamType.PeakHeight));

        Assert.Equal(100, s0.At(0));
        Assert.True(double.IsNaN(s1.At(0)));
        Assert.Equal(200, s2.At(0));
        Assert.True(double.IsNaN(s3.At(0)));
    }

    [Fact]
    public void FeatureIndex_RoundTrips()
    {
        for (int c = 0; c < Channels; c++)
            foreach (ParamType p in Enum.GetValues<ParamType>())
            {
                var fi = ChannelDataBuffer.FeatureIndex(c, p);
                Assert.Equal(c * ChannelDataBuffer.ParamCount + (int)p, fi);
            }
    }
}
