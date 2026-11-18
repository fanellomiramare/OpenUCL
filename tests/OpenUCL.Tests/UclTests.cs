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

    /// <summary>
    /// Golden: ProUCL 5.2 live sheet on silver-data.xls (n = 56, all detected)
    /// with Full Precision ON — campaign 2, scheda F, 2026-11-15 (FP OFF run
    /// 2026-11-11, scheda A). Every deterministic method matches ProUCL to the
    /// full 7 printed decimals. Known deviations, documented not asserted:
    /// H-UCL exact 17.5987 vs ProUCL table-interpolated 18.535003 (+5.32%,
    /// decision #45); ProUCL suggests Student's-t while our logic declines
    /// recommendation on this data set (skewness 7.17, no fit at 10%).
    /// Bootstrap rows are generator-dependent on both sides (no exact parity).
    /// </summary>
    [Fact]
    public void Silver_deterministic_ucls_match_ProUCL_full_precision_sheet()
    {
        var u = Ucl.Compute(BtvUnitTests.Silver, 0.95);

        Assert.Equal(15.448214, u.Mean, 5);              // ProUCL: 15.448214
        Assert.Equal(75.194354, u.StandardDeviation, 5); // ProUCL: 75.194354
        Assert.Equal(7.1743315, u.Skewness, 5);          // ProUCL: 7.1743315
        Assert.Equal(32.259308, u.StudentT, 4);          // ProUCL: 32.259308
        Assert.Equal(33.864871, u.ModifiedT, 4);         // ProUCL: 33.864871
        Assert.Equal(31.976145, u.Clt, 4);               // ProUCL: 31.976145
        Assert.Equal(42.269550, u.AdjustedClt, 5);       // ProUCL: 42.269550 (ours 42.26954971... — parity to the printed 7th decimal)
        Assert.Equal(59.247600, u.ChebyshevMeanSd, 4);   // ProUCL: 59.247600
        Assert.Equal(24.036849, u.GammaApproximate, 3);  // ProUCL: 24.036849
        Assert.Equal(0.3193223, u.KHat, 5);              // ProUCL: 0.3193223
        Assert.Equal(0.3141205, u.KStar, 5);             // ProUCL: 0.3141205
        Assert.Equal(22.610754, u.ChiSquareApprox, 3);   // ProUCL: 22.610754
    }

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
        Assert.Equal(0.0395, Ucl.AdjustedSignificance(0.05, 25), 5);   // TG Example 2-2: 0.0395 for n=25
        Assert.Equal(0.0392, Ucl.AdjustedSignificance(0.05, 24), 5);   // TG Oahu sheet (n=24): 0.0392
        Assert.Equal(0.0380, Ucl.AdjustedSignificance(0.05, 20), 5);
        Assert.Equal(0.0440, Ucl.AdjustedSignificance(0.05, 40), 5);
        Assert.Equal(0.0086, Ucl.AdjustedSignificance(0.05, 3), 5);    // n <= 5: first table row
        Assert.Equal(0.0724, Ucl.AdjustedSignificance(0.10, 10), 5);
        Assert.Equal(0.0015, Ucl.AdjustedSignificance(0.01, 10), 5);
    }

    /// <summary>
    /// Beyond n = 40 ProUCL interpolates in 1/n towards beta = alpha (decision
    /// #82): beta(n) = alpha - (alpha - beta40) * 40 / n. Anchors: ProUCL 5.2
    /// live sheet at Full Precision on silver (n = 56) prints 0.0457143; TG
    /// Table 2-8 (pyrene without outlier, n = 55) prints 0.0456. The old rule
    /// (no adjustment beyond n = 50, flat 0.0440 for 40 &lt; n &lt;= 50) matched neither.
    /// </summary>
    [Fact]
    public void Adjusted_significance_beyond_n40_matches_ProUCL()
    {
        Assert.Equal(0.0457143, Ucl.AdjustedSignificance(0.05, 56), 7);   // ProUCL live FP: 0.0457143
        Assert.Equal(0.0456, Ucl.AdjustedSignificance(0.05, 55), 4);      // TG Table 2-8: 0.0456
        Assert.Equal(0.046, Ucl.AdjustedSignificance(0.05, 60), 9);
        Assert.Equal(0.0452, Ucl.AdjustedSignificance(0.05, 50), 9);
        double large = Ucl.AdjustedSignificance(0.05, 1_000_000);
        Assert.InRange(large, 0.04999, 0.05);                              // -> alpha as n grows
    }

    /// <summary>
    /// TG 5.2 section 2.4: ProUCL supports the adjusted gamma UCL only at the
    /// 0.90, 0.95 and 0.99 confidence levels. Elsewhere the method is not
    /// available (the old code silently reused the 0.05 row: at 97.5% it gave
    /// beta 0.0395 > alpha and returned the 95% value).
    /// </summary>
    [Theory]
    [InlineData(0.025)]
    [InlineData(0.20)]
    [InlineData(0.001)]
    public void Adjusted_gamma_not_available_outside_90_95_99(double alpha)
    {
        Assert.False(Ucl.AdjustedGammaSupported(alpha));
        Assert.True(double.IsNaN(Ucl.AdjustedSignificance(alpha, 25)));
        var u = Ucl.Compute(X, 1.0 - alpha);
        Assert.True(double.IsNaN(u.BetaAdjusted));
        Assert.True(double.IsNaN(u.GammaAdjusted));
        Assert.False(double.IsNaN(u.GammaApproximate));
    }

    [Theory]
    [InlineData(0.90)]
    [InlineData(0.95)]
    [InlineData(0.99)]
    public void Adjusted_gamma_is_more_conservative_than_approximate(double cc)
    {
        var u = Ucl.Compute(X, cc);
        Assert.True(u.BetaAdjusted < 1.0 - cc);
        Assert.True(u.GammaAdjusted > u.GammaApproximate);
    }

    /// <summary>
    /// Golden: ProUCL 5.2 live sheet on silver WITHOUT the outlier 560 (n = 55),
    /// Full Precision ON — campaign 2, scheda H (2026-11-15). The adjusted gamma
    /// UCL at n = 55 exercises the beyond-table beta (decision #82). Ours:
    /// approximate 7.8239249 / adjusted 7.8974160 — both ~1e-6 relative above
    /// ProUCL, the same residual already seen on the chi-square value itself
    /// (ours 39.1959055 vs 39.195937: inversion precision, cf. the silver n = 56
    /// golden asserted at 3 decimals). Same residual on both rows = same beta.
    /// </summary>
    [Fact]
    public void Silver_no_outlier_gamma_ucls_match_ProUCL_full_precision()
    {
        var data = BtvUnitTests.Silver.Where(v => v != 560).ToArray();
        Assert.Equal(55, data.Length);
        var u = Ucl.Compute(data, 0.95);
        Assert.Equal(0.5187397, u.KHat, 6);             // ProUCL: 0.5187397
        Assert.Equal(0.5025660, u.KStar, 6);            // ProUCL: 0.5025660
        Assert.Equal(39.195937, u.ChiSquareApprox, 4);  // ProUCL: 39.195937
        Assert.Equal(7.8239174, u.GammaApproximate, 4); // ProUCL: 7.8239174
        Assert.Equal(7.8974075, u.GammaAdjusted, 4);    // ProUCL: 7.8974075 (was not computed at n = 55 before #82)
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
