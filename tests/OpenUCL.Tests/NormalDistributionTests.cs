using OpenUCL.Core;
using Xunit;

namespace OpenUCL.Tests;

public class NormalDistributionTests
{
    [Theory]
    [InlineData(0.5, 0.0)]
    [InlineData(0.975, 1.959963984540054)]
    [InlineData(0.95, 1.6448536269514722)]
    [InlineData(0.99, 2.3263478740408408)]
    [InlineData(0.8413447460685429, 1.0)]
    [InlineData(0.022750131948179212, -2.0)]
    public void Quantile_matches_known_probit_values(double p, double expected)
    {
        Assert.Equal(expected, NormalDistribution.Quantile(p), 12);
    }

    [Fact]
    public void Quantile_is_antisymmetric()
    {
        Assert.Equal(-NormalDistribution.Quantile(0.9), NormalDistribution.Quantile(0.1), 12);
    }

    [Fact]
    public void Cdf_matches_known_values()
    {
        Assert.Equal(0.5, NormalDistribution.Cdf(0), 15);
        Assert.Equal(0.8413447460685429, NormalDistribution.Cdf(1.0), 12);
        Assert.Equal(0.9772498680518208, NormalDistribution.Cdf(2.0), 12);
        Assert.Equal(0.9986501019683699, NormalDistribution.Cdf(3.0), 12);
        Assert.Equal(0.999999999979452, NormalDistribution.Cdf(6.6), 11); // 1-Phi = 2.0548e-11
    }

    [Fact]
    public void Quantile_and_Cdf_are_inverses()
    {
        foreach (var p in new[] { 1e-9, 1e-6, 0.001, 0.025, 0.3, 0.5, 0.77, 0.999, 1 - 1e-9 })
        {
            double z = NormalDistribution.Quantile(p);
            Assert.Equal(p, NormalDistribution.Cdf(z), 9);
        }
    }
}
