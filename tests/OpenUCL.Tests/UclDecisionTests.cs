using OpenUCL.Core;
using Xunit;

namespace OpenUCL.Tests;

/// <summary>
/// ProUCL 5.2 UCL recommendation logic (decision #84): the flowcharts of TG 5.2
/// Appendix C (Figures C-1..C-5) transcribed in <see cref="UclDecision"/>. The
/// branch tests drive the decision with synthetic GOF outcomes; the anchors run
/// the whole chain on data sets with a live ProUCL 5.2 suggestion.
/// </summary>
public class UclDecisionTests
{
    private static UclDecisionGof Gof(bool normal = false, bool gamma = false, bool lognormal = false,
        double kHat = double.NaN, double kStar = double.NaN, double sdLog = double.NaN) =>
        new(30, normal, gamma, lognormal, kHat, kStar, sdLog);

    private static readonly UclCandidates All = new()
    {
        StudentT = 10, AdjustedGamma = 12, ApproximateGamma = 11, HUcl = 13,
        KmT = 20, KmBca = 21, KmBootstrapT = 22, KmHUcl = 23,
        KmAdjustedGamma = 24, KmApproximateGamma = 25, GrosAdjustedGamma = 26, GrosApproximateGamma = 27,
    };

    private static UclMethod[] Methods(UclRecommendation r) => r.Suggestions.Select(s => s.Method).ToArray();

    // ---- Figure C-1, no NDs ----

    [Fact]
    public void Normal_data_get_students_t_even_when_gamma_also_fits()
    {
        // the old logic tested gamma first and returned the adjusted gamma UCL
        var r = UclDecision.Recommend(0.95, 30, false, Gof(normal: true, gamma: true, lognormal: true, kHat: 5), All);
        Assert.Equal(new[] { UclMethod.StudentT }, Methods(r));
        Assert.Equal("normal", r.Distribution);
        Assert.Equal(10, r.Primary!.Value);
    }

    [Fact]
    public void Not_normal_and_n_below_10_gives_no_recommendation()
    {
        var r = UclDecision.Recommend(0.95, 9, false, Gof(gamma: true, kHat: 2), All);
        Assert.Empty(r.Suggestions);
        Assert.Equal("undetermined", r.Distribution);
    }

    [Fact]
    public void No_discernible_distribution_gives_students_t()
    {
        var r = UclDecision.Recommend(0.95, 56, false, Gof(), All);
        Assert.Equal(new[] { UclMethod.StudentT }, Methods(r));
        Assert.Equal("nonparametric", r.Distribution);
    }

    // ---- Figure C-2: gamma, no NDs (k-hat) ----

    [Theory]
    [InlineData(0.40, 60, UclMethod.AdjustedGamma)]     // k-hat < 0.5: adjusted at any n
    [InlineData(0.80, 49, UclMethod.AdjustedGamma)]     // n < 50
    [InlineData(0.80, 50, UclMethod.ApproximateGamma)]  // k-hat >= 0.5 and n >= 50
    [InlineData(0.50, 80, UclMethod.ApproximateGamma)]
    public void Gamma_without_nds_follows_figure_c2(double kHat, int n, UclMethod expected)
    {
        var r = UclDecision.Recommend(0.95, n, false, Gof(gamma: true, kHat: kHat), All);
        Assert.Equal(new[] { expected }, Methods(r));
        Assert.Equal("gamma", r.Distribution);
    }

    [Fact]
    public void Gamma_without_nds_and_invalid_adjusted_gives_no_recommendation()
    {
        var c = All with { AdjustedGamma = double.NaN };
        Assert.Empty(UclDecision.Recommend(0.95, 25, false, Gof(gamma: true, kHat: 2), c).Suggestions);
        c = All with { ApproximateGamma = -1 };
        Assert.Empty(UclDecision.Recommend(0.95, 60, false, Gof(gamma: true, kHat: 2), c).Suggestions);
    }

    // ---- Figure C-3: lognormal, no NDs ----

    [Theory]
    [InlineData(28, 2.5, UclMethod.HUcl)]       // n >= 28: H-UCL whatever the sd(log)
    [InlineData(20, 1.0, UclMethod.HUcl)]       // n < 28, sd(log) < 1.5
    [InlineData(20, 1.6, UclMethod.StudentT)]   // n < 28, sd(log) >= 1.5
    public void Lognormal_without_nds_follows_figure_c3(int n, double sdLog, UclMethod expected)
    {
        var r = UclDecision.Recommend(0.95, n, false, Gof(lognormal: true, sdLog: sdLog), All);
        Assert.Equal(new[] { expected }, Methods(r));
        Assert.Equal("lognormal", r.Distribution);
    }

    [Fact]
    public void Lognormal_without_nds_and_h_flag_gives_no_recommendation()
    {
        Assert.Empty(UclDecision.Recommend(0.95, 1002, false, Gof(lognormal: true, sdLog: 1), All).Suggestions);
    }

    // ---- Figure C-1, with NDs ----

    [Fact]
    public void Normal_detects_get_km_t()
    {
        var r = UclDecision.Recommend(0.95, 24, true, Gof(normal: true, gamma: true, kStar: 1.7), All);
        Assert.Equal(new[] { UclMethod.KmT }, Methods(r));
    }

    [Fact]
    public void Nonparametric_detects_get_km_t()
    {
        Assert.Equal(new[] { UclMethod.KmT }, Methods(UclDecision.Recommend(0.95, 40, true, Gof(), All)));
    }

    // ---- Figure C-4: gamma with NDs (k* of the detects) ----

    [Fact]
    public void Gamma_with_nds_follows_figure_c4()
    {
        var g = Gof(gamma: true, kStar: 1.7);
        Assert.Equal(new[] { UclMethod.KmAdjustedGamma, UclMethod.GrosAdjustedGamma },
            Methods(UclDecision.Recommend(0.95, 24, true, g, All)));
        Assert.Equal(new[] { UclMethod.GrosAdjustedGamma },
            Methods(UclDecision.Recommend(0.95, 24, true, g, All with { KmAdjustedGamma = double.NaN })));
        Assert.Equal(new[] { UclMethod.KmApproximateGamma, UclMethod.GrosApproximateGamma },
            Methods(UclDecision.Recommend(0.95, 50, true, g, All)));

        var low = Gof(gamma: true, kStar: 0.8);
        Assert.Equal(new[] { UclMethod.KmBootstrapT, UclMethod.KmHUcl },
            Methods(UclDecision.Recommend(0.95, 14, true, low, All)));
        Assert.Equal(new[] { UclMethod.KmAdjustedGamma },
            Methods(UclDecision.Recommend(0.95, 15, true, low, All)));
        Assert.Equal(new[] { UclMethod.KmApproximateGamma },
            Methods(UclDecision.Recommend(0.95, 30, true, low, All with { KmAdjustedGamma = double.NaN })));
        Assert.Equal(new[] { UclMethod.KmApproximateGamma },
            Methods(UclDecision.Recommend(0.95, 50, true, low, All)));
    }

    // ---- Figure C-5: lognormal with NDs ----

    [Fact]
    public void Lognormal_with_nds_follows_figure_c5()
    {
        Assert.Equal(new[] { UclMethod.KmHUcl },
            Methods(UclDecision.Recommend(0.95, 75, true, Gof(lognormal: true, sdLog: 2.5), All)));
        Assert.Equal(new[] { UclMethod.KmT, UclMethod.KmBca },
            Methods(UclDecision.Recommend(0.95, 74, true, Gof(lognormal: true, sdLog: 1.2), All)));
        Assert.Empty(UclDecision.Recommend(0.95, 74, true, Gof(lognormal: true, sdLog: 1.5), All).Suggestions);
    }

    /// <summary>User Guide 5.2: recommendations are provided only for 95% UCLs.</summary>
    [Theory]
    [InlineData(0.90)]
    [InlineData(0.99)]
    public void Recommendations_only_at_95_percent(double cc)
    {
        var r = UclDecision.Recommend(cc, 30, false, Gof(normal: true), All);
        Assert.Empty(r.Suggestions);
        Assert.Equal("normal", r.Distribution);   // the distribution branch is still reported
    }

    [Fact]
    public void Students_t_may_be_negative()
    {
        var c = All with { StudentT = -0.5 };
        Assert.Equal(-0.5, UclDecision.Recommend(0.95, 30, false, Gof(normal: true), c).Primary!.Value);
    }

    // ---- anchors on real data ----

    private static UclRecommendation RunNoNd(double[] data)
    {
        var u = Ucl.Compute(data, 0.95);
        var gof = UclDecision.EvaluateGof(data);
        double meanLog = data.Select(v => Math.Log(v)).Average();
        double h = LandExact.LandUcl(meanLog, gof.SdLog, data.Length, 0.95);
        return UclDecision.Recommend(0.95, data.Length, false, gof, new UclCandidates
        {
            StudentT = u.StudentT, AdjustedGamma = u.GammaAdjusted, ApproximateGamma = u.GammaApproximate, HUcl = h,
        });
    }

    /// <summary>
    /// ProUCL 5.2 live sheet (campaign 1, scheda A, and FP campaign 2, scheda F):
    /// silver (n = 56) not normal at 1%, not gamma at 5%, not lognormal at 10%
    /// (ProUCL: "Data Not Lognormal at 10%") — suggests "95% Student's-t UCL
    /// 32.259308". Before decision #84 our CLI declined any recommendation.
    /// </summary>
    [Fact]
    public void Silver_suggests_students_t_like_ProUCL_live()
    {
        var r = RunNoNd(BtvUnitTests.Silver);
        Assert.Equal("nonparametric", r.Distribution);
        Assert.Equal(UclMethod.StudentT, r.Primary!.Method);
        Assert.Equal(32.259308, r.Primary.Value, 4);
    }

    /// <summary>
    /// ProUCL 5.2 live sheet (scheda B / H): silver without the outlier (n = 55)
    /// is approximately lognormal at 10% (S-W passes, Lilliefors fails) and not
    /// gamma: ProUCL suggests the 95% H-UCL (its table-interpolated 11.107569;
    /// ours is the exact 10.7191 — decision #45).
    /// </summary>
    [Fact]
    public void Silver_no_outlier_suggests_h_ucl_like_ProUCL_live()
    {
        var r = RunNoNd(BtvUnitTests.Silver.Where(v => v != 560).ToArray());
        Assert.Equal("lognormal", r.Distribution);
        Assert.Equal(UclMethod.HUcl, r.Primary!.Method);
        Assert.Equal(10.7191, r.Primary.Value, 3);
    }

    /// <summary>TG Example 2-2 (Table 2-6): "Suggested UCL to Use: 95% Adjusted Gamma UCL 74.27".</summary>
    [Fact]
    public void Example_2_2_suggests_adjusted_gamma_like_TG_table_2_6()
    {
        double[] x =
        {
            0.3489, 0.8526, 2.5445, 2.5602, 3.3706, 4.8911, 5.0930, 5.6408, 7.0407,
            14.1715, 15.2608, 17.6214, 18.7690, 23.6804, 25.0461, 31.7720, 60.7066,
            67.0926, 72.6243, 78.8357, 80.0867, 113.0230, 117.0360, 164.3302, 169.8303,
        };
        var r = RunNoNd(x);
        Assert.Equal("gamma", r.Distribution);
        Assert.Equal(UclMethod.AdjustedGamma, r.Primary!.Method);
        Assert.Equal(74.27, r.Primary.Value, 2);
    }
}

/// <summary>
/// The bootstrap generator is the canonical xoshiro256** (decision #86), so any
/// third party can reproduce the resampling streams from the printed seed.
/// </summary>
public class SeededRngTests
{
    [Fact]
    public void Matches_the_xoshiro256starstar_reference_vector()
    {
        // Reference sequence for the raw state {1, 2, 3, 4} (Blackman and Vigna's
        // xoshiro256starstar.c; the same vector the Rust rand_xoshiro tests use).
        var rng = new SeededRng(1, 2, 3, 4);
        ulong[] expected = { 11520, 0, 1509978240, 1215971899390074240, 1216172134540287360, 607988272756665600 };
        foreach (ulong e in expected) Assert.Equal(e, rng.NextUInt64());
    }

    [Fact]
    public void Doubles_cover_the_whole_unit_interval()
    {
        // The pre-#86 scrambler left output bits 48-51 at zero: NextDouble could
        // only fall in [k/4096, k/4096 + 1/65536), 1/16 of [0, 1).
        var rng = new SeededRng(381);
        int outside = 0;
        for (int i = 0; i < 10_000; i++)
            if (rng.NextDouble() % (1.0 / 4096) >= 1.0 / 65536) outside++;
        Assert.InRange(outside, 9000, 9700);   // uniform: ~93.75%
    }
}

/// <summary>
/// Degenerate inputs (audit 2026-11-18 [E1]) and the nondetect UCLs added with
/// the decision logic (KM gamma, KM H-UCL; decision #84).
/// </summary>
public class DegenerateAndKmUclTests
{
    private static ValueObservation D(double v) => ValueObservation.Detect(v);
    private static ValueObservation Nd(double dl) => ValueObservation.Nondetect(dl);

    [Fact]
    public void Identical_values_are_rejected_with_a_clear_message()
    {
        double[] c = { 5, 5, 5, 5, 5, 5 };
        Assert.Contains("identical", Assert.Throws<ArgumentException>(() => GoodnessOfFit.ShapiroWilk(c)).Message);
        Assert.Contains("identical", Assert.Throws<ArgumentException>(() => Ucl.Compute(c, 0.95)).Message);
        Assert.Contains("identical", Assert.Throws<ArgumentException>(() => Btv.Compute(c)).Message);
        var gof = UclDecision.EvaluateGof(c);
        Assert.False(gof.Normal || gof.Gamma || gof.Lognormal);
    }

    [Fact]
    public void Conclude_without_shapiro_wilk_uses_lilliefors_alone()
    {
        var pass = new LillieforsResult(3000, 0.001);
        var fail = new LillieforsResult(3000, 0.5);
        Assert.Equal(GofConclusion.Normal, GoodnessOfFit.Conclude(null, pass, 0.05));
        Assert.Equal(GofConclusion.NotNormal, GoodnessOfFit.Conclude(null, fail, 0.05));
    }

    /// <summary>
    /// TG Table 4-4 (Oahu, n = 24): "Gamma Kaplan-Meier (KM) Statistics" —
    /// k hat (KM) 1.771, nu hat (KM) 85.02, 95% Gamma Approximate KM-UCL 1.246,
    /// 95% Gamma Adjusted KM-UCL 1.27 (beta at n = 24: 0.0392).
    /// </summary>
    [Fact]
    public void Km_gamma_ucls_match_TG_table_4_4()
    {
        var un = UclWithNd.Compute(KaplanMeierTests.Oahu(), 0.95, 2000, 381);
        Assert.Equal(1.771, un.KmGammaK, 3);
        Assert.Equal(85.02, un.KmGammaNu, 2);
        Assert.Equal(1.246, un.KmGammaApproximate, 3);
        Assert.Equal(1.27, un.KmGammaAdjusted, 2);
    }

    /// <summary>
    /// TG Table 4-4: KM on the logged Oahu data — KM SD (logged) 0.547 and
    /// 95% H-UCL (KM-Log) 1.155 with the table-interpolated critical H 2.023.
    /// Ours: the same KM log estimates and the exact H (2.0068, decision #45),
    /// giving 1.1529.
    /// </summary>
    [Fact]
    public void Km_h_ucl_matches_TG_table_4_4_within_the_h_table_deviation()
    {
        var un = UclWithNd.Compute(KaplanMeierTests.Oahu(), 0.95, 2000, 381);
        Assert.Equal(-0.236, un.KmLogMean, 3);
        Assert.Equal(0.547, un.KmLogSd, 3);
        Assert.Equal(LandExact.LandCriticalH(24, un.KmLogSd, 0.95), un.KmH, 12);
        Assert.Equal(1.155, un.KmHUcl, 0.005);
    }

    /// <summary>
    /// Small data set with many NDs: some bootstrap resamples contain only NDs
    /// ((3/6)^6 = 1.6% of 2000 runs). They are redrawn (decision #85) — before,
    /// the first one threw and every ND-aware UCL of the series was dropped.
    /// </summary>
    [Fact]
    public void Km_bootstrap_survives_all_nd_resamples()
    {
        var obs = new List<ValueObservation> { D(5), D(7), D(9), Nd(1), Nd(1), Nd(2) };
        var un = UclWithNd.Compute(obs, 0.95, 2000, 381);
        Assert.True(double.IsFinite(un.KmPercentileBootstrap));
        Assert.True(double.IsFinite(un.KmBootstrapT));
        Assert.True(double.IsFinite(un.KmT));
    }
}
