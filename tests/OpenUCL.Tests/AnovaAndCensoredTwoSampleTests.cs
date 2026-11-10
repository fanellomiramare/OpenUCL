using OpenUCL.Core;
using Xunit;

namespace OpenUCL.Tests;

public class AnovaTests
{
    // Classic 3-group example: group means 2, 4, 6 with equal spreads.
    private static readonly double[][] Groups =
    {
        new[] { 1.0, 2.0, 3.0 },     // mean 2
        new[] { 3.0, 4.0, 5.0 },     // mean 4
        new[] { 5.0, 6.0, 7.0 },     // mean 6
    };

    [Fact]
    public void Hand_computed_anova_table()
    {
        var a = Anova.Compute(Groups, 0.95);
        Assert.Equal(3, a.Groups);
        Assert.Equal(9, a.N);
        // grand mean = 4; SS between = 3 * (2-4)^2 + 3 * 0 + 3 * (6-4)^2 = 24
        Assert.Equal(24.0, a.SSBetween, 10);
        // SS within: sum of squared deviations {1,2,3} = 2 per group -> 6 total
        Assert.Equal(6.0, a.SSWithin, 10);
        Assert.Equal(2, a.DfBetween);
        Assert.Equal(6, a.DfWithin);
        // F = (24/2) / (6/6) = 12
        Assert.Equal(12.0, a.F, 10);
        Assert.InRange(a.PValue, 0.005, 0.015);   // P(F(2,6) > 12) ~ 0.008
        Assert.True(a.RejectNull);
    }

    [Fact]
    public void Kruskal_wallis_max_h_on_separated_groups()
    {
        // non-overlapping groups: all A below all B below all C -> H = N - 1 = 6
        double[][] separated =
        {
            new[] { 1.0, 2.0, 3.0 },
            new[] { 7.0, 8.0, 9.0 },
            new[] { 13.0, 14.0, 15.0 },
        };
        var a = Anova.Compute(separated, 0.95);
        // perfectly separated, no ties: H = 12/(9*10) * 279 - 30 = 7.2 (standard formula)
        Assert.Equal(7.2, a.KwH, 10);
        Assert.True(a.KwPValue < 0.05);
        Assert.True(a.KwRejectNull);
    }

    [Fact]
    public void Identical_groups_give_zero_statistics()
    {
        double[] g = { 1, 2, 3, 4 };
        var a = Anova.Compute(new[] { g, (double[])g.Clone() }, 0.95);
        Assert.Equal(0.0, a.SSBetween, 10);
        Assert.Equal(0.0, a.F, 10);
        Assert.Equal(1.0, a.PValue, 10);
        Assert.Equal(0.0, a.KwH, 10);
        Assert.False(a.KwRejectNull);
    }
}

public class CensoredTwoSampleTestsCore
{
    private static ValueObservation D(double v) => ValueObservation.Detect(v);
    private static ValueObservation Nd(double dl) => ValueObservation.Nondetect(dl);

    [Fact]
    public void Gehan_hand_computed_simple()
    {
        // 10 vs 10: bg = 10 x 5 (ranks 1..10, avg 5.5), site = 10 x 10 (ranks 11..20, avg 15.5)
        // TG score a(R) = 2R - N - 1 = 2R - 21: bg = -10, site = +10
        // G = 10*10 / sqrt(20/100 * (10*100 + 10*100)) = 100 / sqrt(400) = 5.0
        var bg10 = Enumerable.Repeat(5.0, 10).Select(D).ToList();
        var site10 = Enumerable.Repeat(10.0, 10).Select(D).ToList();
        var g = CensoredTwoSample.Gehan(site10, bg10, upperTail: true);
        Assert.Equal(5.0, g.Statistic, 10);
        Assert.True(g.RejectNull);
    }

    [Fact]
    public void Requires_ten_per_sample()
    {
        var small = Enumerable.Repeat(1.0, 5).Select(D).ToList();
        var big = Enumerable.Repeat(1.0, 12).Select(D).ToList();
        Assert.Throws<ArgumentException>(() => CensoredTwoSample.Gehan(small, big, true));
        Assert.Throws<ArgumentException>(() => CensoredTwoSample.TaroneWare(big, small, true));
    }
}
