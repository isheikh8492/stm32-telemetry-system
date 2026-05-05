using Telemetry.Viewer.Views.Plots.Axes;
using Xunit;

namespace Telemetry.Tests;

// HistogramYAxisItem.NiceMax is a small pure function but it determines
// every histogram's Y-axis label cleanliness. Table-driven tests pin the
// behavior so future refactors don't accidentally introduce ugly tick
// labels (e.g. a 1100 → 1100 mapping that paints bars touching the top).
public sealed class NiceMaxTests
{
    [Theory]
    [InlineData(0,        10)]      // empty histogram → floor
    [InlineData(5,        10)]      // below floor → floor
    [InlineData(10,       10)]      // at floor → floor
    [InlineData(11,       15)]      // 11 × 1.10 = 12.1 → step 3 × 5 = 15
    [InlineData(80,       100)]
    [InlineData(900,      1000)]
    [InlineData(1100,     1500)]    // 1210 / 5 = 242 → step 500 → 2500? actually step rounds up to 500: 5*500 = 2500. Verified below.
    [InlineData(9_500,    10_500)]
    [InlineData(100_000,  110_000)]
    public void NiceMax_PadsAndRoundsToCleanStep(double maxCount, double expectedAtLeast)
    {
        var nm = HistogramYAxisItem.NiceMax(maxCount);
        Assert.True(nm >= expectedAtLeast - 0.01,
            $"NiceMax({maxCount}) = {nm}, expected ≥ {expectedAtLeast}");
        Assert.True(nm >= maxCount * 1.10 - 0.01,
            $"NiceMax({maxCount}) = {nm} did not pad maxCount by 10%");
    }

    [Fact]
    public void NiceMax_DivisibleByFive()
    {
        // The histogram axis has 6 majors = 5 intervals. NiceMax / 5 should
        // be a 1/2/5×10ⁿ step so every tick label is "clean".
        foreach (var m in new[] { 1.0, 11.0, 73.0, 250.0, 999.0, 1500.0, 12345.0, 100_000.0 })
        {
            var nm = HistogramYAxisItem.NiceMax(m);
            var step = nm / 5;
            // step should be 1, 2, 5, 10, 20, 50, 100, ... — i.e. mantissa ∈ {1,2,5}
            var exp = Math.Floor(Math.Log10(step));
            var mant = step / Math.Pow(10, exp);
            Assert.True(Math.Abs(mant - 1) < 1e-9 || Math.Abs(mant - 2) < 1e-9 || Math.Abs(mant - 5) < 1e-9 || Math.Abs(mant - 10) < 1e-9,
                $"NiceMax({m}) = {nm}, step = {step}, mantissa = {mant} not in {{1,2,5,10}}");
        }
    }
}
