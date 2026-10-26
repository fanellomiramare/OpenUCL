using OpenUCL.Core;
using Xunit;

namespace OpenUCL.Tests;

public class SpecialFunctionsTests
{
    [Theory]
    [InlineData(1.0, -0.5772156649015329)]   // -Euler-Mascheroni
    [InlineData(0.5, -1.9635100260214235)]   // -gamma - 2 ln 2
    [InlineData(10.0, 2.2517525890667211)]
    public void Digamma_matches_known_values(double k, double expected)
    {
        // Choi-Wette truncation (the same approximation ProUCL uses): ~1e-7 accuracy.
        Assert.Equal(expected, SpecialFunctions.Digamma(k), 6);
    }

    [Theory]
    [InlineData(1.0, 1.6449340668482264)]    // pi^2/6
    [InlineData(0.5, 4.9348022005446793)]    // pi^2/2
    [InlineData(10.0, 0.10516633568168562)]
    public void Trigamma_matches_known_values(double k, double expected)
    {
        Assert.Equal(expected, SpecialFunctions.Trigamma(k), 6);
    }

    [Fact]
    public void Gamma_cdf_known_values()
    {
        // k=1: exponential.
        Assert.Equal(1 - Math.Exp(-1.0), GammaDistribution.Cdf(1.0, 1.0, 1.0), 12);
        // k=2: 1 - (1+x) e^-x.
        Assert.Equal(1 - 2.0 / Math.E, GammaDistribution.Cdf(1.0, 2.0, 1.0), 12);
        Assert.Equal(0.0, GammaDistribution.Cdf(0.0, 3.0, 2.0), 12);
    }
}

public class GammaMLETests
{
    // Exact exponential (gamma k=1, theta=1) order-statistic quantiles: the MLE of k
    // must come out close to 1, theta close to 1.
    private static double[] ExponentialQuantiles(int n) =>
        Enumerable.Range(1, n).Select(i => -Math.Log(1.0 - i / (n + 1.0))).ToArray();

    [Fact]
    public void MLE_solves_the_likelihood_equation()
    {
        // Whatever the sample, the converged k must satisfy the MLE equation
        // ln(k) - Psi(k) = M (TG eq. 2-21) to the accuracy of the Choi-Wette Psi.
        var fit = GammaDistribution.FitMLE(ExponentialQuantiles(60));
        Assert.Equal(fit.M, Math.Log(fit.KHat) - SpecialFunctions.Digamma(fit.KHat), 8);

        // Sanity: exponential-quantile data (k=1 population) fits k near 1
        // (quantile-grid samples deviate slightly; wide bounds intentional).
        Assert.InRange(fit.KHat, 0.9, 1.3);
        Assert.InRange(fit.KStar, 0.85, 1.25);
    }

    [Fact]
    public void Bias_correction_matches_TG_equation_2_29()
    {
        var fit = GammaDistribution.FitMLE(ExponentialQuantiles(10));
        Assert.Equal(7.0 / 10.0 * fit.KHat + 2.0 / 30.0, fit.KStar, 14);
        Assert.Equal(fit.Mean / fit.KStar, fit.ThetaStar, 14);
    }

    [Fact]
    public void Non_positive_data_throws()
    {
        Assert.Throws<ArgumentException>(() => GammaDistribution.FitMLE(new double[] { 1, 2, 0 }));
        Assert.Throws<ArgumentException>(() => GammaDistribution.FitMLE(new double[] { 1, 2 }));
    }
}

public class GammaGofTests
{
    private static double[] ExponentialQuantiles(int n) =>
        Enumerable.Range(1, n).Select(i => -Math.Log(1.0 - i / (n + 1.0))).ToArray();

    [Fact]
    public void Exponential_quantiles_pass_gamma_gof()
    {
        var g = GammaGof.Compute(ExponentialQuantiles(50));
        Assert.True(g.KS_D < 0.05, $"D={g.KS_D}");
        Assert.True(g.AD_A2 < 0.3, $"A2={g.AD_A2}");
        Assert.True(g.KS_D <= g.CriticalValue(GammaGofTest.KolmogorovSmirnov, 0.01));
        Assert.True(g.AD_A2 <= g.CriticalValue(GammaGofTest.AndersonDarling, 0.01));
    }

    [Fact]
    public void Left_skewed_data_rejects_gamma_gof()
    {
        // Gamma densities are always right-skewed; a strongly left-skewed positive
        // sample must be rejected. (Perfectly normal data, instead, are legitimately
        // consistent with a large-k gamma and are NOT expected to be rejected.)
        var g = GammaGof.Compute(LeftSkewedPositive(40));
        Assert.True(g.AD_A2 > g.CriticalValue(GammaGofTest.AndersonDarling, 0.05),
            $"A2={g.AD_A2} should exceed its critical value");
    }

    private static double[] LeftSkewedPositive(int n)
    {
        var q = ExponentialQuantiles(n);
        double max = q[^1];
        return q.Select(v => 10.0 - v + (max - 4.0)).ToArray(); // mirror and keep well positive
    }

    /// <summary>
    /// Spot checks quoted in the ProUCL Technical Guide body (independent of the table
    /// transcription): §2.2.2 quotes Table A-3 (A-D, alpha=0.05, n=7): k=10,20,50 ->
    /// 0.708, 0.707, 0.708; and Table A-4 (K-S, alpha=0.05, n=200): k=0.025 -> 0.070489,
    /// k=0.05 -> 0.07466 (the known interpolated cell).
    /// </summary>
    [Fact]
    public void Critical_value_tables_match_TG_body_quotes()
    {
        Assert.Equal(0.708, GammaGofCriticalValues.Lookup(GammaGofTest.AndersonDarling, 0.05, 7, 10), 9);
        Assert.Equal(0.707, GammaGofCriticalValues.Lookup(GammaGofTest.AndersonDarling, 0.05, 7, 20), 9);
        Assert.Equal(0.708, GammaGofCriticalValues.Lookup(GammaGofTest.AndersonDarling, 0.05, 7, 50), 9);
        Assert.Equal(0.070489, GammaGofCriticalValues.Lookup(GammaGofTest.KolmogorovSmirnov, 0.05, 200, 0.025), 11);
        Assert.Equal(0.074659, GammaGofCriticalValues.Lookup(GammaGofTest.KolmogorovSmirnov, 0.05, 200, 0.05), 9);
        // Table A-4 n=30 first two cells are equal in the printed table (as extracted).
        Assert.Equal(0.187026, GammaGofCriticalValues.Lookup(GammaGofTest.KolmogorovSmirnov, 0.05, 30, 0.025), 11);
        Assert.Equal(0.187026, GammaGofCriticalValues.Lookup(GammaGofTest.KolmogorovSmirnov, 0.05, 30, 0.05), 11);
    }

    [Fact]
    public void Critical_value_interpolation_behaves()
    {
        // Exact grid node: A-D, alpha=0.05, n=10, k=1 -> 0.748.
        Assert.Equal(0.748, GammaGofCriticalValues.Lookup(GammaGofTest.AndersonDarling, 0.05, 10, 1), 12);
        // Interpolated in n between 50 and 60 at k=0.5: (0.814+0.816)/2.
        Assert.Equal(0.815, GammaGofCriticalValues.Lookup(GammaGofTest.AndersonDarling, 0.05, 55, 0.5), 12);
        // Interpolated in n at k=0.2: (0.912084+0.914286)/2.
        Assert.Equal(0.913185, GammaGofCriticalValues.Lookup(GammaGofTest.AndersonDarling, 0.05, 55, 0.2), 9);
        // Clamped below the smallest n.
        Assert.Equal(GammaGofCriticalValues.Lookup(GammaGofTest.KolmogorovSmirnov, 0.01, 5, 1),
                     GammaGofCriticalValues.Lookup(GammaGofTest.KolmogorovSmirnov, 0.01, 3, 1), 12);
        // Clamped above the largest k.
        Assert.Equal(GammaGofCriticalValues.Lookup(GammaGofTest.KolmogorovSmirnov, 0.01, 100, 50),
                     GammaGofCriticalValues.Lookup(GammaGofTest.KolmogorovSmirnov, 0.01, 100, 80), 12);
    }

    [Fact]
    public void Unsupported_alpha_throws()
    {
        var g = GammaGof.Compute(ExponentialQuantiles(10));
        Assert.Throws<ArgumentOutOfRangeException>(() => g.CriticalValue(GammaGofTest.AndersonDarling, 0.025));
    }
}
