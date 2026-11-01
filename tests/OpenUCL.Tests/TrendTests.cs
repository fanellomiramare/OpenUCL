using OpenUCL.Core;
using Xunit;

namespace OpenUCL.Tests;

public class TrendTests
{
    [Fact]
    public void Perfectly_increasing_series_has_max_mk_and_upward_trend()
    {
        double[] data = { 1, 2, 3, 4, 5, 6, 7 };
        var t = Trend.Compute(data);
        Assert.Equal(21.0, t.MkS, 10);                 // all pairs positive: n(n-1)/2
        Assert.True(t.MkZ > 3.0);
        Assert.True(t.MkPValueTwoSided < 0.005);       // continuity-corrected max: ~0.0027
        Assert.Equal("increasing", t.MkTrend);
        Assert.Equal(1.0, t.TheilSenSlope, 12);        // pairwise slopes all = 1
        Assert.Equal(1.0, t.OlsSlope, 12);
        Assert.Equal(1.0, t.OlsR2, 12);
        Assert.True(t.OlsSlopePValue < 1e-6);
    }

    [Fact]
    public void Perfectly_decreasing_series_is_downward()
    {
        double[] data = { 7, 6, 5, 4, 3, 2, 1 };
        var t = Trend.Compute(data);
        Assert.Equal(-21.0, t.MkS, 10);
        Assert.Equal("decreasing", t.MkTrend);
        Assert.Equal(-1.0, t.TheilSenSlope, 12);
    }

    [Fact]
    public void Theil_sen_robust_to_one_outlier()
    {
        // line y = 10 + 2t with one extreme outlier at t = 4
        double[] data = { 10, 12, 14, 60, 18 };
        var t = Trend.Compute(data);
        Assert.Equal(2.0, t.TheilSenSlope, 1);         // median of pairwise slopes still 2
    }

    [Fact]
    public void Ties_use_mk_tie_correction()
    {
        // constant series: S = 0, tie term consumes the variance -> z = 0
        double[] data = { 3, 3, 3, 3, 3 };
        var t = Trend.Compute(data);
        Assert.Equal(0.0, t.MkS, 10);
        Assert.Equal(0.0, t.MkZ, 12);
        Assert.Equal(1.0, t.MkPValueTwoSided, 12);
        Assert.Equal("no trend", t.MkTrend);
        Assert.Equal(0.0, t.TheilSenSlope, 12);
    }

    [Fact]
    public void Requires_four_observations()
    {
        Assert.Throws<ArgumentException>(() => Trend.Compute(new double[] { 1, 2, 3 }));
    }
}
