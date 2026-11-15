using OpenUCL.Core;
using Xunit;

namespace OpenUCL.Tests;

public class NoncentralTTests
{
    /// <summary>
    /// Golden anchors from ProUCL Technical Guide Tables 3-1/3-2 (Aluminum, n=24 and
    /// n=21): "Tolerance Factor K (For UTL) 2.309 / 2.371".
    /// </summary>
    [Theory]
    [InlineData(24, 2.309)]
    [InlineData(21, 2.371)]
    public void Tolerance_factor_matches_TG_tables(int n, double expected)
    {
        Assert.Equal(expected, NoncentralT.ToleranceFactor(n, 0.95, 0.95), 2);
    }

    [Fact]
    public void Central_case_reduces_to_normal_quantile_for_large_n()
    {
        // As n grows, K -> z(0.95) = 1.6449.
        Assert.True(Math.Abs(NoncentralT.ToleranceFactor(100000000, 0.95, 0.95) - 1.644854) < 0.001);
    }
}

public class BtvUnitTests
{
    /// <summary>silver-data.xls (n=56, all detected), the ProUCL sample data set
    /// used by several live-sheet goldens (USL, UTL/K, UCL full precision).</summary>
    internal static readonly double[] Silver =
    {
        0.1,0.1,0.1,0.1,0.2,0.2,0.2,0.2,0.2,0.3,0.5,0.5,0.7,0.8,1,1,1,1,1,1,1,1,1,1,1,1,1,1.2,1.4,1.5,2,2,2,2,2,2.5,2.7,3.2,4.4,5,5,5,5,6,10,10,10,10,10,10,20,20,20,25,90,560
    };

    [Fact]
    public void ProUcl_percentile_interpolation()
    {
        double[] sorted = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10,
                            11, 12, 13, 14, 15, 16 };
        // Excel PERCENTILE.INC / ProUCL 4.1+ (TG text; scheda J parity):
        // h = 1 + p(n-1) with linear interpolation (n=16, p=0.95 -> h = 15.25).
        Assert.Equal(15.25, Btv.PercentileProUcl(sorted, 0.95), 12);
        // n=2, p=0.1 -> h = 1.1 (in range, no clamp).
        Assert.Equal(5.1, Btv.PercentileProUcl(new double[] { 5, 6 }, 0.1), 12);
    }

    [Fact]
    public void Achieved_cc_for_max_of_24_is_0708()
    {
        // TG Table 3-5 (Lead): UTL based on the maximum (r = 24 ascending) achieves CC 0.708.
        Assert.Equal(0.708, Btv.AchievedCC(24, 24, 0.95), 3);
        Assert.Equal(1 - Math.Pow(0.95, 24), Btv.AchievedCC(24, 24, 0.95), 12);
        // the minimum can hardly cover the 95th percentile of the population
        Assert.True(Btv.AchievedCC(24, 1, 0.95) < 1e-20);
    }

    /// <summary>
    /// Golden: ProUCL 5.2 live BTV sheet on silver-data.xls (n=56, all
    /// observations as detected; run 2026-11-11 Full Precision OFF, re-run
    /// 2026-11-15 campaign 2 scheda G with Full Precision ON).
    /// DISCOVERY (FP ON): d2max = 3.0007348 vs our Grubbs closed form
    /// 3.0007349 — EXACT parity to the printed 7th decimal: the "3.001 vs
    /// 3.00073" delta was pure display rounding, there is NO deviation in
    /// d2max, and every USL (normal/lognormal/WH/HW) matches to 7-8 digits.
    /// The gamma percentile runs on k*/theta* for the same parity (see
    /// Btv.cs). The only real deviation on the sheet remains K (Natrella
    /// 2.0319819 vs exact 2.037739, -0.2825%), which propagates to the UTLs
    /// (e.g. UTL normal 168.24178 vs our 168.675 — fully explained: with
    /// K_Natrella our formula reproduces 168.24178 exactly). The exact-K
    /// claim's history (pre-2026-11-11 underflow bug for n > 31) is in
    /// decision #54.
    /// </summary>
    [Fact]
    public void Usl_silver_n56_matches_ProUCL_live_sheet()
    {
        double[] silver = Silver;
        var b = Btv.Compute(silver, 0.95, 0.95, 1);

        Assert.Equal(3.0007349, b.D2Max, 5);             // ProUCL FP: 3.0007348
        Assert.Equal(241.08653, b.UslNormal, 3);         // ProUCL FP: 241.08653
        Assert.Equal(343.34052, b.UslLognormal, 3);      // ProUCL FP: 343.34052
        Assert.Equal(129.78276, b.UslGammaWh, 3);        // ProUCL FP: 129.78276
        Assert.Equal(131.60603, b.UslGammaHw, 3);        // ProUCL FP: 131.60603
        Assert.Equal(560, b.UslNonparametric, 0);

        // Full-precision percentile and coverage parity (scheda G).
        Assert.Equal(139.13192, b.PercentileNormal, 3);   // ProUCL FP: 139.13192
        Assert.Equal(32.187273, b.PercentileLognormal, 3); // ProUCL FP: 32.187273
        Assert.Equal(69.624347, b.PercentileGamma, 3);    // ProUCL FP: 69.624346 (k*/theta*)
        Assert.Equal(0.9434384, b.UtlAchievedCC, 5);      // ProUCL FP: 0.9434384
        Assert.Equal(34.75, b.UplNonparametric, 2);       // ProUCL FP: 34.750000
        Assert.Equal(346.12633, b.UplChebyshev, 3);       // ProUCL FP: 346.12633
        Assert.Equal(21.25, b.PercentileNonparametric, 2); // ProUCL FP: 21.250000 (Excel PERCENTILE.INC, scheda J)
    }

    /// <summary>
    /// Golden: TG Table 4-4 (Oahu, n=24, 13 NDs, Example 4-5) — the KM-based
    /// UCL95 values: KM (t) 1.231, KM (z) 1.22, Chebyshev 95% 1.667, SE 0.165,
    /// bootstrap-t 1.368 (pivotal t* on KM mean/SE, TG 4.7; seed-dependent),
    /// plus the gamma-KM percentile via the chi-square route (TG Table 5-2:
    /// 95% percentile 2.328; ours 2.3401, -0.5% from ProUCL's internal k-hat).
    /// The SE fix (2026-11-14): the previous implementation paired the area
    /// through distinct i-1 with the weight of distinct i (off-by-one) and
    /// gave SE ~ 0.109 -> KM(t) ~ 1.137. Data embedded; runs on CI.
    /// </summary>
    [Fact]
    public void Km_ucls_oahu_match_TG_table_4_4()
    {
        static ValueObservation D(double v) => ValueObservation.Detect(v);
        static ValueObservation Nd(double dl) => ValueObservation.Nondetect(dl);
        var oahu = new List<ValueObservation>
        {
            D(0.5), D(0.5), D(0.5), D(0.6), D(0.7), D(0.7), D(0.9), Nd(0.9), Nd(1), Nd(1),
            Nd(1), Nd(1), D(1.5), D(1.7), Nd(2), Nd(2), Nd(2), Nd(2), Nd(2), Nd(2),
            Nd(2), Nd(2), D(2.8), D(3.2)
        };

        var km = Censored.KaplanMeier(oahu);
        Assert.Equal(0.948958, km.Mean, 5);
        Assert.Equal(0.164684, km.StandardErrorOfMean, 4);   // TG Table 4-4: SE 0.165
        Assert.Equal(0.713016, km.Sd, 4);                    // TG: SD 0.713

        var un = UclWithNd.Compute(oahu, 0.95);
        Assert.InRange(un.KmT, 1.228, 1.235);                // TG Table 4-4: 1.231
        Assert.InRange(un.KmZ, 1.215, 1.225);                // TG Table 4-4: 1.22
        Assert.InRange(un.KmChebyshev, 1.663, 1.670);        // TG Table 4-4: 1.667
        Assert.InRange(un.KmBootstrapT, 1.31, 1.40);         // TG Table 4-4: bootstrap-t 1.368 (seed-dependent; ours 1.345 @ seed 381)

        var bn = BtvWithNd.Compute(oahu, 0.95, 0.95, 1);
        Assert.InRange(bn.PercentileGammaChi, 2.33, 2.35);   // TG Table 5-2: 2.328
    }

    [Fact]
    public void Tolerance_factor_n56_matches_ProUCL_live_sheet()
    {
        double k = NoncentralT.ToleranceFactor(56, 0.95, 0.95);
        Assert.Equal(2.037739, k, 5);
        Assert.InRange(k, 2.02, 2.06);   // ProUCL (Natrella approx., FP ON 2026-11-15): 2.0319819 → exact −0.2825%
    }

    [Fact]
    public void Nonparametric_upl_uses_n_plus_one_position()
    {
        // TG eq. 3-17: n=25, 90% -> m = 23.4 -> x(23) + 0.4 (x(24) - x(23)).
        double[] sorted = Enumerable.Range(1, 25).Select(i => (double)i * 10).ToArray();
        double upl = Btv.PositionInterp(sorted, (25 + 1.0) * 0.90);
        Assert.Equal(230 + 0.4 * 10, upl, 12);
        // 99% exceeds n: clamped to the largest order statistic.
        Assert.Equal(250, Btv.PositionInterp(sorted, (25 + 1.0) * 0.99), 12);
    }

    [Fact]
    public void Hand_computed_normal_btv_suite()
    {
        // Textbook sample: mean 5, sd sqrt(32/7) = 2.13809, n = 8.
        double[] data = { 2, 4, 4, 4, 5, 5, 7, 9 };
        var b = Btv.Compute(data, 0.95, 0.95, 5);

        double t = StudentT.Quantile(0.95, 7);
        Assert.Equal(5 + t * Math.Sqrt(32.0 / 7) * Math.Sqrt(1 + 1 / 8.0), b.UplNormal1, 10);
        Assert.Equal(5 + NoncentralT.ToleranceFactor(8, 0.95, 0.95) * Math.Sqrt(32.0 / 7), b.UtlNormal, 10);
        Assert.Equal(5 + NormalDistribution.Quantile(0.95) * Math.Sqrt(32.0 / 7), b.PercentileNormal, 10);
        // UPL5 uses the Bonferroni-adjusted t quantile.
        Assert.Equal(5 + StudentT.Quantile(1 - 0.05 / 5, 7) * Math.Sqrt(32.0 / 7) * Math.Sqrt(1 + 1 / 8.0),
            b.UplNormalK, 10);
        Assert.Equal(5 + t * Math.Sqrt(32.0 / 7) * Math.Sqrt(1 / 5.0 + 1 / 8.0), b.UplNormalKMean, 10);
        // Chebyshev UPL (TG section 3.5.4.1).
        Assert.Equal(5 + Math.Sqrt(1 / 0.05 - 1) * Math.Sqrt(32.0 / 7) * Math.Sqrt(1 + 1 / 8.0),
            b.UplChebyshev, 10);
    }
}

/// <summary>
/// Golden tests against ProUCL Technical Guide 5.2 Tables 3-1..3-5, computed on the
/// SuperFund.xls sample data set shipped with ProUCL (the Example 2-4/3-x data set).
/// The columns are embedded below (read from the shipped workbook) so the goldens
/// run on every machine, CI included.
/// </summary>
public class BtvGoldenTests
{
    // SuperFund.xls columns (n = 24 each), read from the workbook shipped with ProUCL.
    private static readonly double[] Aluminum = { 6280, 3830, 3900, 5130, 9310, 15300, 9730, 7840, 10400, 16200, 6350, 10700, 15400, 12500, 2850, 9040, 2700, 1710, 3430, 6790, 11600, 4110, 7230, 4610 };
    private static readonly double[] Chromium = { 8.7, 8.1, 11, 5.1, 12, 20, 12, 11, 13, 20, 9.8, 14, 17, 15, 8.4, 14, 4.5, 3, 4, 11, 16.4, 7.6, 35.5, 6.1 };
    private static readonly double[] Arsenic = { 1.3, 1.2, 2, 1.2, 3.2, 5.9, 2.3, 1.9, 2.9, 3.7, 1.8, 2.3, 2.4, 2.2, 1.1, 3.7, 1.1, 1, 1.5, 2.6, 2.4, 1.1, 2.1, 0.66 };
    private static readonly double[] Lead = { 16, 6.4, 4.9, 8.3, 18, 14, 12, 8.7, 11, 12, 14, 14, 19, 21, 16, 25, 20, 11, 6.3, 13, 98.5, 53.3, 109, 8.3 };

    [Fact]
    public void Aluminum_normal_utl_and_upl_match_TG_table_3_1()
    {
        var data = Aluminum;
        Assert.Equal(24, data.Length);

        var b = Btv.Compute(data, 0.95, 0.95);
        Assert.Equal(2.309, b.ToleranceFactorK, 3);       // TG: K = 2.309; ProUCL FP: 2.3090000 (tabled at n<=30; ours exact 2.30929, +0.0125%)
        Assert.InRange(b.UtlNormal, 17630, 17640);        // TG: 95% UTL 17635; ProUCL FP: 17634.505 (K-prop delta ~1.3)
        Assert.Equal(15247.630, b.UplNormal1, 1);         // ProUCL FP: 15247.630; TG: 15248
        Assert.InRange(BtvNormalPercentile(data, 0.90), 13248, 13258);   // TG: 90% percentile 13254; ProUCL FP: 13253.570
        Assert.InRange(BtvNormalPercentile(data, 0.95), 14798, 14808);   // TG: 95% percentile 14803; ProUCL FP: 14802.653
        Assert.InRange(BtvNormalPercentile(data, 0.99), 17702, 17714);   // TG: 99% percentile 17708; ProUCL FP: 17708.474

        // Full Precision ON (campaign 2, scheda J) parity extras:
        Assert.Equal(16569.057, b.PercentileGamma, 0);    // ProUCL FP: 16569.057 (k*/theta*); TG: 16569
        Assert.Equal(15385, b.PercentileNonparametric, 1); // ProUCL FP: 15385 (Excel PERCENTILE.INC)
        Assert.Equal(26758.317, b.UplChebyshev, 2);       // ProUCL FP: 26758.317
        Assert.Equal(0.7080110, b.UtlAchievedCC, 5);      // ProUCL FP: 0.7080110
    }

    private static double BtvNormalPercentile(double[] data, double p)
    {
        double mean = 0;
        foreach (var v in data) mean += v;
        mean /= data.Length;
        double ss = 0;
        foreach (var v in data) { double d = v - mean; ss += d * d; }
        double sd = Math.Sqrt(ss / (data.Length - 1));
        return mean + sd * NormalDistribution.Quantile(p);
    }

    [Fact]
    public void Chromium_lognormal_limits_match_TG_table_3_3()
    {
        var data = Chromium;
        Assert.Equal(24, data.Length);

        var b = Btv.Compute(data, 0.95, 0.95, futureK: 5);
        Assert.Equal(38.3, b.UtlLognormal, 0.5);              // TG: 38.3
        Assert.Equal(27.87, b.UplLognormal1, 0.5);            // TG: 27.87
        Assert.Equal(43.96, b.UplNormalK * 0 + LogNormalUplK(data, 5), 0.5);  // TG: 43.96
        Assert.Equal(16.66, LogNormalUplKMean(data, 5), 0.5); // TG: 16.66
    }

    private static double LogNormalUplK(double[] data, int k)
    {
        int n = data.Length;
        var logs = data.Select(v => Math.Log(v)).ToArray();
        double mean = 0;
        foreach (var v in logs) mean += v;
        mean /= n;
        double ss = 0;
        foreach (var v in logs) { double d = v - mean; ss += d * d; }
        double sd = Math.Sqrt(ss / (n - 1));
        return Math.Exp(mean + sd * StudentT.Quantile(1 - 0.05 / k, n - 1) * Math.Sqrt(1 + 1.0 / n));
    }

    private static double LogNormalUplKMean(double[] data, int k)
    {
        int n = data.Length;
        var logs = data.Select(v => Math.Log(v)).ToArray();
        double mean = 0;
        foreach (var v in logs) mean += v;
        mean /= n;
        double ss = 0;
        foreach (var v in logs) { double d = v - mean; ss += d * d; }
        double sd = Math.Sqrt(ss / (n - 1));
        return Math.Exp(mean + sd * StudentT.Quantile(0.95, n - 1) * Math.Sqrt(1.0 / k + 1.0 / n));
    }

    [Fact]
    public void Arsenic_gamma_limits_match_TG_table_3_4()
    {
        var data = Arsenic;
        Assert.Equal(24, data.Length);

        var b = Btv.Compute(data, 0.95, 0.95);
        Assert.Equal(4.153, BtvKHat(data), 2);                            // TG: k-hat 4.153; ProUCL FP: 4.1529767
        // Full Precision ON (campaign 2, scheda J, 2026-11-15): EXACT parity on
        // every gamma limit — and the old "TG internally inconsistent" claim
        // about the HW UPL was WRONG: ProUCL 5.2 live computes 4.3973341,
        // identical to our eq. 3-16 value (the earlier 4.63 hand computation
        // was an arithmetic error, now removed).
        Assert.Equal(4.3451609, b.UplGammaWh, 3);                         // ProUCL FP: 4.3451609; TG: 4.345
        Assert.Equal(4.3973341, b.UplGammaHw, 3);                         // ProUCL FP: 4.3973341; TG: 4.397
        Assert.Equal(5.3816574, b.UtlGammaWh, 2);                         // ProUCL FP: 5.3816574 (K-prop delta ~6e-4)
        Assert.Equal(5.5239468, b.UtlGammaHw, 2);                         // ProUCL FP: 5.5239468
        Assert.Equal(4.2642964, b.PercentileGamma, 3);                    // ProUCL FP: 4.2642964 (k*/theta*); TG: 4.264
    }

    private static double BtvKHat(double[] data)
    {
        var fit = GammaDistribution.FitMLE(data);
        return fit.KHat;
    }

    [Fact]
    public void Lead_nonparametric_limits_match_TG_table_3_5()
    {
        var data = Lead;
        Assert.Equal(24, data.Length);

        var sorted = (double[])data.Clone();
        Array.Sort(sorted);

        var b = Btv.Compute(sorted, 0.95, 0.95);
        Assert.Equal(sorted[^1], b.UtlNonparametric, 10);                 // TG: UTL = 109 (max)
        Assert.Equal(24, b.UtlNonparametricOrder);                        // TG: order r = 24
        Assert.Equal(0.708, b.UtlAchievedCC, 3);                          // TG: achieved CC 0.708 (ProUCL FP: 0.7080110)
        Assert.Equal(sorted[^1], b.UslNonparametric, 10);                 // TG: USL = 109
        // UPL95: position (n+1)*0.95 = 23.75 -> x(23) + 0.75*(x(24) - x(23)).
        // CONFIRMED at Full Precision ON (scheda J): ProUCL live prints
        // 106.37500000 — the TG table's 106.49 comes from rounded inputs.
        Assert.Equal(106.375, b.UplNonparametric, 1);
        // Chebyshev UPL: mean + sqrt(19)*sd*sqrt(1+1/24); ProUCL FP: 141.84560
        // (the earlier comment value 141.76 was a stale hand computation).
        Assert.Equal(141.84560, b.UplChebyshev, 3);
    }
}
