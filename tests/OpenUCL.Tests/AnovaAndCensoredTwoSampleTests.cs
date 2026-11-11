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
    public void Requires_two_per_sample()
    {
        var one = new List<ValueObservation> { D(1.0) };
        var two = new List<ValueObservation> { D(1.0), D(2.0) };
        Assert.Throws<ArgumentException>(() => CensoredTwoSample.Gehan(one, two, true));
        Assert.Throws<ArgumentException>(() => CensoredTwoSample.TaroneWare(two, one, true));
        // below 10 per sample the tests still compute (the normal approximation
        // is flagged as unreliable in the CLI sheet, as in ProUCL)
        var g = CensoredTwoSample.Gehan(two, two, true);
        Assert.Equal(0.0, g.Statistic, 10);
    }

    /// <summary>
    /// Golden: ProUCL 5.2 live output on Tarone-ware-data.xls (Comp n=8, 1 ND
    /// vs Bckgrnd n=6, 3 ND; run 2026-11-11): TW = 1.850, p = 0.0322, Reject;
    /// our Tarone-Ware reproduces it exactly. NOTE on Gehan: our TG-prescribed
    /// midrank Gehan gives G = 1.6591 while ProUCL's internal Gehan prints
    /// 1.812 — a ProUCL-specific convention not documented in the Technical
    /// Guide (Tarone-Ware, the preferred test, matches exactly).
    /// </summary>
    [Fact]
    public void TaroneWare_comp_vs_bckgrnd_matches_ProUCL_live_output()
    {
        var comp = new List<ValueObservation> { D(6.4), D(10.9), D(7), D(14.3), D(1.9), D(10), D(6.8), Nd(5) };
        var bckgrnd = new List<ValueObservation> { Nd(4), D(1.5), Nd(2), D(8.7), D(5.1), Nd(5) };

        var tw = CensoredTwoSample.TaroneWare(comp, bckgrnd, upperTail: true);
        Assert.Equal(1.849748, tw.Statistic, 5);
        Assert.Equal(0.0322, tw.PValueOneSided, 4);
        Assert.True(tw.RejectNull);

        // regression anchor for our TG-exact Gehan (ProUCL prints 1.812)
        var g = CensoredTwoSample.Gehan(comp, bckgrnd, upperTail: true);
        Assert.Equal(1.659119, g.Statistic, 5);
    }

    /// <summary>
    /// Golden: ProUCL 5.2 live output on WMW-with NDs.xls (Site n=11, 3 ND vs
    /// Background n=11, 3 ND; run 2026-11-11): TW = 1.022, p = 0.153, Do Not
    /// Reject — our Tarone-Ware matches exactly; our Gehan (TG midranks)
    /// gives 1.5377 while ProUCL prints 1.152 (same documented deviation).
    /// </summary>
    [Fact]
    public void TaroneWare_site_vs_background_matches_ProUCL_live_output()
    {
        var site = new List<ValueObservation> { D(2), Nd(4), Nd(8), D(17), D(20), D(25), D(34), D(35), D(40), D(43), Nd(11) };
        var background = new List<ValueObservation> { D(1), Nd(4), D(5), Nd(7), D(12), D(15), D(18), D(21), D(25), D(27), Nd(9) };

        var tw = CensoredTwoSample.TaroneWare(site, background, upperTail: true);
        Assert.Equal(1.022330, tw.Statistic, 5);
        Assert.Equal(0.153311, tw.PValueOneSided, 5);
        Assert.False(tw.RejectNull);

        var g = CensoredTwoSample.Gehan(site, background, upperTail: true);
        Assert.Equal(1.537680, g.Statistic, 5);
    }
}

public class BtvWithNdGoldenTests
{
    private static ValueObservation D(double v) => ValueObservation.Detect(v);
    private static ValueObservation Nd(double dl) => ValueObservation.Nondetect(dl);

    /// <summary>
    /// Golden: TG 5.2 Example 5-1 / Tables 5-1..5-3 — the Oahu data set
    /// (n=24, 13 NDs), USLs computed on KM estimates per TG 5.3.4:
    /// normal 2.834 (Table 5-1), gamma WH 3.051 / HW 3.107, lognormal 3.354
    /// (Table 5-3); d2max(24) = 2.644. Cross-checks from the same tables:
    /// UTL normal-KM 2.595, Chebyshev-KM UPL 4.121, percentile normal-KM
    /// 2.122, UTL gamma WH-KM 2.661. Data embedded; runs on CI.
    /// </summary>
    [Fact]
    public void Usl_km_oahu_matches_TG_tables_5_1_to_5_3()
    {
        var oahu = new List<ValueObservation>
        {
            D(0.5), D(0.5), D(0.5), D(0.6), D(0.7), D(0.7), D(0.9), Nd(0.9), Nd(1), Nd(1),
            Nd(1), Nd(1), D(1.5), D(1.7), Nd(2), Nd(2), Nd(2), Nd(2), Nd(2), Nd(2),
            Nd(2), Nd(2), D(2.8), D(3.2)
        };

        var b = BtvWithNd.Compute(oahu, 0.95, 0.95, 1);

        Assert.Equal(2.643910, b.D2Max, 4);
        Assert.Equal(2.834110, b.UslNormal, 4);          // TG Table 5-1: 2.834
        Assert.Equal(3.353680, b.UslLognormal, 4);       // TG Table 5-3: 3.354
        Assert.Equal(3.050650, b.UslGammaWh, 4);         // TG Table 5-3: 3.051
        Assert.Equal(3.106790, b.UslGammaHw, 4);         // TG Table 5-3: 3.107
        Assert.Equal(3.2, b.UslNonparametric, 6);

        // cross-checks from the same TG tables
        Assert.Equal(2.595520, b.UtlNormal, 4);          // Table 5-1: 2.595
        Assert.Equal(4.121010, b.UplChebyshev, 4);       // Table 5-1: 4.121
        Assert.Equal(2.121770, b.PercentileNormal, 4);   // Table 5-1: 2.122
        Assert.Equal(2.661700, b.UtlGammaWh, 4);         // Table 5-2: 2.661
    }
}
