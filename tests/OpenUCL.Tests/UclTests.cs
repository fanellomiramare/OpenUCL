using OpenUCL.Core;
using Xunit;

namespace OpenUCL.Tests;

/// <summary>
/// Golden tests against ProUCL Technical Guide 5.2, Example 2-2 (Tables 2-5 and 2-6):
/// the fully enumerated 25-observation data set with published statistics.
/// </summary>
public class UclGoldenTests
{
    // TG Example 2-2, enumerated on guide page 98 (PDF 98).
    private static readonly double[] X =
    {
        0.3489, 0.8526, 2.5445, 2.5602, 3.3706, 4.8911, 5.0930, 5.6408, 7.0407,
        14.1715, 15.2608, 17.6214, 18.7690, 23.6804, 25.0461, 31.7720, 60.7066,
        67.0926, 72.6243, 78.8357, 80.0867, 113.0230, 117.0360, 164.3302, 169.8303,
    };

    private static UclResult Act() => Ucl.Compute(X, 0.95);

    [Fact]
    public void Summary_statistics_match_TG_table()
    {
        var u = Act();
        Assert.Equal(25, u.N);
        Assert.Equal(44.09, u.Mean, 2);          // TG: 44.09
        Assert.Equal(51.34, u.StandardDeviation, 2);
        Assert.Equal(1.294, u.Skewness, 3);
    }

    [Fact]
    public void Gamma_MLE_matches_TG_table_2_6()
    {
        var u = Act();
        Assert.Equal(0.643, u.KHat, 2);          // TG: k hat (MLE) 0.643
        Assert.Equal(0.592, u.KStar, 2);         // TG: k star 0.592
        Assert.Equal(68.58, u.ThetaHat, 1);      // TG: theta hat 68.58
        Assert.Equal(74.42, u.ThetaStar, 1);     // TG: theta star 74.42
    }

    [Fact]
    public void Adjusted_significance_interpolates_table_2_2()
    {
        Assert.Equal(0.0395, Ucl.AdjustedSignificance(0.05, 25), 5);   // TG: 0.0395 for n=25
        Assert.Equal(0.0380, Ucl.AdjustedSignificance(0.05, 20), 5);
        Assert.Equal(0.0440, Ucl.AdjustedSignificance(0.05, 40), 5);
        Assert.Equal(0.05, Ucl.AdjustedSignificance(0.05, 60), 5);     // >= 50: no adjustment
    }

    [Fact]
    public void Gamma_ucls_match_TG_table_2_6()
    {
        var u = Act();
        Assert.Equal(18.2, u.ChiSquareApprox, 1);     // TG: approximate chi-square (0.05) 18.2
        Assert.Equal(17.59, u.ChiSquareAdjusted, 0.5);// TG: adjusted chi-square 17.59
        Assert.Equal(71.77, u.GammaApproximate, 0.5); // TG: 71.77
        Assert.Equal(74.27, u.GammaAdjusted, 0.5);    // TG: 74.27
    }

    [Fact]
    public void Normal_and_nonparametric_ucls_match_TG_table_2_5()
    {
        var u = Act();
        // Deterministic methods (bootstrap methods tested separately: they depend on RNG).
        Assert.Equal(61.66, u.StudentT, 0.1);         // TG text: Student's t-UCL of 61.66
        Assert.Equal(60.98, u.Clt, 0.1);              // TG: 95% CLT UCL 60.98
        Assert.Equal(88.85, u.ChebyshevMeanSd, 0.1);  // TG: 95% Chebyshev (Mean, Sd) 88.85
        Assert.Equal(61.66, u.Jackknife, 0.1);        // TG: 95% Jackknife 61.66 (equals t-UCL)
    }

    /// <summary>
    /// Bootstrap results are seed-dependent: ProUCL's generator differs from ours, so the
    /// TG values (percentile 60.32, standard 60.57, bootstrap-t 65.58, BCA 64.8) are
    /// verified within a tolerance. With a fixed seed the values are perfectly
    /// reproducible run-to-run and platform-to-platform.
    /// </summary>
    [Fact]
    public void Bootstrap_ucls_close_to_TG_table_2_5()
    {
        var u = Act();
        Assert.InRange(u.PercentileBootstrap, 55, 66);   // TG: 60.32
        Assert.InRange(u.StandardBootstrap, 55, 66);     // TG: 60.57
        Assert.InRange(u.BootstrapT, 60, 71);            // TG: 65.58
        Assert.InRange(u.BcaBootstrap, 59, 70);          // TG: 64.8
    }

    [Fact]
    public void Bootstrap_is_reproducible_for_a_fixed_seed()
    {
        var a = Ucl.Compute(X, 0.95, 500, 381);
        var b = Ucl.Compute(X, 0.95, 500, 381);
        Assert.Equal(a.PercentileBootstrap, b.PercentileBootstrap, 12);
        Assert.Equal(a.BcaBootstrap, b.BcaBootstrap, 12);
        Assert.Equal(a.BootstrapT, b.BootstrapT, 12);
        // a different seed must produce (almost surely) different bootstrap values
        var c = Ucl.Compute(X, 0.95, 500, 382);
        Assert.NotEqual(a.PercentileBootstrap, c.PercentileBootstrap);
    }

    [Fact]
    public void Modified_t_exceeds_student_t_for_right_skew()
    {
        var u = Act();
        Assert.True(u.ModifiedT > u.StudentT);
        Assert.True(u.AdjustedClt > u.Clt);
    }
}

public class StudentTTests
{
    [Theory]
    [InlineData(7, 1.8945786050613050)]    // t(0.95, 7): the classic textbook value
    [InlineData(24, 1.7108820798995846)]   // t(0.95, 24)
    public void Quantile_matches_known_values(double df, double expected)
    {
        Assert.Equal(expected, StudentT.Quantile(0.95, df), 10);
    }

    [Fact]
    public void Quantile_and_cdf_are_inverses()
    {
        foreach (var p in new[] { 0.05, 0.5, 0.9, 0.975 })
        {
            double q = StudentT.Quantile(p, 12);
            Assert.Equal(p, StudentT.Cdf(q, 12), 8);
        }
        // extreme tail: the inverse BetaI limits the round-trip to ~1e-7
        double q999 = StudentT.Quantile(0.999, 12);
        Assert.Equal(0.999, StudentT.Cdf(q999, 12), 6);
        Assert.Equal(0.0, StudentT.Quantile(0.5, 5), 12);
    }
}
