using OpenUCL.Core;
using Xunit;

namespace OpenUCL.Tests;

public class DescriptiveTests
{
    // Classic textbook sample: mean 5, sample variance 32/7, median 4.5.
    private static readonly double[] Textbook = { 2, 4, 4, 4, 5, 5, 7, 9 };

    [Fact]
    public void Textbook_sample_matches_reference_values()
    {
        var s = Descriptive.Compute(Textbook);

        Assert.Equal(8, s.N);
        Assert.Equal(2, s.Min);
        Assert.Equal(9, s.Max);
        Assert.Equal(5.0, s.Mean, 12);
        Assert.Equal(32.0 / 7, s.Variance, 12);
        Assert.Equal(Math.Sqrt(32.0 / 7), s.StandardDeviation, 12);
        Assert.Equal(4.5, s.Median, 12);
        Assert.Equal(4.0, s.Q1, 12);  // type-7 percentile: h = 7*0.25 = 1.75
        Assert.Equal(5.5, s.Q3, 12);
        Assert.Equal(Math.Sqrt(32.0 / 7) / 5, s.CoefficientOfVariation, 12);
    }

    [Fact]
    public void Single_observation_has_nan_spread()
    {
        var s = Descriptive.Compute(new double[] { 42 });

        Assert.Equal(1, s.N);
        Assert.Equal(42, s.Mean);
        Assert.Equal(42, s.Min);
        Assert.Equal(42, s.Max);
        Assert.True(double.IsNaN(s.Variance));
        Assert.True(double.IsNaN(s.StandardDeviation));
        Assert.Equal(42, s.Median);
    }

    [Fact]
    public void Empty_sample_throws()
    {
        Assert.Throws<ArgumentException>(() => Descriptive.Compute(Array.Empty<double>()));
    }

    [Fact]
    public void Percentiles_are_type7_linear_interpolation()
    {
        double[] sorted = { 1, 2, 3, 4 };
        Assert.Equal(1.75, Descriptive.Percentile(sorted, 0.25), 12);
        Assert.Equal(2.5, Descriptive.Percentile(sorted, 0.5), 12);
        Assert.Equal(3.25, Descriptive.Percentile(sorted, 0.75), 12);
        Assert.Equal(1, Descriptive.Percentile(sorted, 0.0), 12);
        Assert.Equal(4, Descriptive.Percentile(sorted, 1.0), 12);
    }
}
