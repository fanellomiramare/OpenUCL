using OpenUCL.Core;
using Xunit;

namespace OpenUCL.Tests;

public class DixonExactTests
{
    /// <summary>
    /// Anchors from the ProUCL TG Example 7-1 (OS_Lead, n=10): 10% 0.409,
    /// 5% 0.477, 1% 0.597 (r11 subrange, one-sided). Published tables round to
    /// three decimals; our integration should land within ~0.002.
    /// </summary>
    [Theory]
    [InlineData(0.10, 0.409)]
    [InlineData(0.05, 0.477)]
    [InlineData(0.01, 0.597)]
    public void Critical_values_match_TG_anchor_n10(double alpha, double expected)
    {
        // r11 at n=10: order statistics (2, 9, 10).
        Assert.Equal(expected, DixonExact.CriticalValue(10, 2, 9, 10, alpha), 2);
    }

    [Fact]
    public void Tail_probability_normalization()
    {
        // R > 0 always (the numerator range is positive): the tail at 0 must be 1.
        Assert.Equal(1.0, DixonExact.TailProbability(10, 2, 9, 10, 0.0), 9);
        Assert.Equal(1.0, DixonExact.TailProbability(5, 1, 4, 5, 0.0), 9);
    }

    [Fact]
    public void Critical_values_are_monotone()
    {
        // larger n -> smaller critical value; smaller alpha -> larger critical value.
        double k10 = DixonExact.CriticalValue(10, 2, 9, 10, 0.05);
        double k25 = DixonExact.CriticalValue(25, 3, 23, 25, 0.05);
        Assert.True(k25 < k10, $"K(25)={k25} should be < K(10)={k10}");
        double a5 = DixonExact.CriticalValue(10, 2, 9, 10, 0.05);
        double a1 = DixonExact.CriticalValue(10, 2, 9, 10, 0.01);
        Assert.True(a1 > a5);
    }

    [Fact]
    public void Outlier_flags_from_exact_criticals()
    {
        // data with a clear high outlier: flagged at all levels with exact criticals.
        double[] data = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 1940 };
        var d = Outliers.Dixon(data);
        Assert.True(d.HighOutlier1);
        Assert.False(d.LowOutlier10);
    }
}

public class TwoSampleTestsTests
{
    private static readonly double[] A = { 1, 2, 3, 4, 5 };       // mean 3, s^2 2.5
    private static readonly double[] B = { 3, 5, 7, 9, 11 };      // mean 7, s^2 10

    [Fact]
    public void Pooled_t_hand_computed()
    {
        var t = TwoSampleTests.TTestEqualVariance(A, B, upperTail: true);
        // sp = 2.5, se = 2.5 * sqrt(2/5) -> t = -4 / 1.5811 = -2.5298
        Assert.Equal(-2.5298, t.Statistic, 3);
        Assert.Equal(8, t.DegreesOfFreedom);
        Assert.False(t.RejectNull);          // t negative on the upper-tail test
        var lower = TwoSampleTests.TTestEqualVariance(A, B, upperTail: false);
        Assert.True(lower.RejectNull);       // p = 0.0179 < 0.05 on the lower tail
    }

    [Fact]
    public void Satterthwaite_hand_computed()
    {
        var t = TwoSampleTests.TTestSatterthwaite(A, B, upperTail: true);
        // se = sqrt(2.5/5 + 10/5) = sqrt(2.5); df = 2.5^2 / (0.5^2/4 + 2^2/4) = 5.8824
        Assert.Equal(-2.5298, t.Statistic, 3);
        Assert.Equal(5.8824, t.DegreesOfFreedom, 3);
    }

    [Fact]
    public void F_test_hand_computed()
    {
        var (f, pTwo, reject, dfLarge, dfSmall) = TwoSampleTests.FTest(A, B);
        Assert.Equal(4.0, f, 10);            // 10 / 2.5
        Assert.Equal(4, dfLarge);
        Assert.Equal(4, dfSmall);
        // two-sided p: 2 * (1 - F_{4,4}(4)) ; F_{4,4}(4) = 0.9028 -> p = 0.1944
        Assert.InRange(pTwo, 0.15, 0.25);
        Assert.False(reject);
    }

    [Fact]
    public void Wmw_z_hand_computed()
    {
        // A occupies pooled positions 1, 2, 3, 5, 6 -> ranks 1, 2, 3.5, 5, 6.5
        // (average ranks on the two tied pairs) -> rankSum1 = 18 -> U1 = 18 - 15 = 3.
        // sigma with tie correction: sqrt(25/12 * (11 - 12/90)) = 4.75803;
        // z = (U1 + 0.5 - n1n2/2) / sigma = -1.8915 (lower tail, continuity +0.5).
        var w = TwoSampleTests.Wmw(A, B, upperTail: false);
        double sigma2 = 25.0 / 12.0 * (11.0 - 12.0 / 90.0);
        double expectedZ = (3.0 + 0.5 - 12.5) / Math.Sqrt(sigma2);
        Assert.Equal(expectedZ, w.Statistic, 10);
        Assert.True(w.RejectNull);
        Assert.Equal(0.0293, w.PValueOneSided, 3);
    }
}
