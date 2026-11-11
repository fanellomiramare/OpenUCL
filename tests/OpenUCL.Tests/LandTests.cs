using OpenUCL.Core;
using Xunit;

namespace OpenUCL.Tests;

/// <summary>
/// Land's exact H-UCL (Land 1971, 1975; TG 5.2 section 2.4.3, eq. 2-36;
/// EPA/OSWER 9285.6-10 exhibits 3-4). H is computed exactly by polar
/// integration of the conditional-t density (same algorithm as the reference
/// EnvStats implementation, verified line-by-line against its R source).
///
/// ProUCL in contrast computes H by interpolation of Land's (1975) tables
/// (its TG 4.0 states only the 90% and 95% levels are supported, the levels
/// present in the tables). Within the table range (n up to ~31) the two agree
/// within table-lookup precision; for n beyond the table range ProUCL's
/// interpolated H is inflated by 5-7% (silver data set, n=55-56). Decision
/// (2026-11-11): OpenUCL ships the exact computation and documents the
/// deviation for large n.
///
/// Golden anchors (raw data sets printed in the public documents):
///  - EPA 2002 exhibit 4 (n=31): H=2.31 (table), UCL=14.4 -> exact 14.345
///  - EPA 2002 exhibit 6 (n=29): UCL=2658 (table)      -> exact 2643.3
///  - TG 5.1 example 2-2 (n=25, ProUCL 5.0): UCL=229.2 -> exact 229.167
///  - TG 5.1 tables 2-3/2-4 (silver, n=56/55): ProUCL prints 18.54/11.11;
///    exact values 17.599/10.719 (ProUCL table interpolation, out of range)
/// </summary>
public class LandExactTests
{
    [Fact]
    public void Epa_2002_worked_example_matches_exhibit_4()
    {
        double h = LandExact.LandCriticalH(31, 0.8995, 0.95);
        Assert.InRange(h, 2.26, 2.36);           // EPA 2002 exhibit: H = 2.31
        double ucl = LandExact.LandUcl(1.8797, 0.8995, 31, 0.95);
        Assert.InRange(ucl, 14.2, 14.5);         // EPA 2002 exhibit: UCL = 14.4
    }

    [Fact]
    public void Epa_2002_exhibit_6_data_set_is_reproduced()
    {
        double[] data = { 107, 175, 1796, 2002, 109, 30, 273, 83, 127, 254, 466, 12,
                          403, 31, 1042, 923, 24, 537, 5667, 59, 158, 59, 353, 10,
                          8, 33, 1129, 3, 279 };
        double meanLog = 0;
        foreach (double v in data) meanLog += Math.Log(v);
        meanLog /= data.Length;
        double ss = 0;
        foreach (double v in data) ss += Math.Pow(Math.Log(v) - meanLog, 2);
        double sdLog = Math.Sqrt(ss / (data.Length - 1));

        Assert.Equal(4.969028, meanLog, 5);      // exhibit prints 4.9690
        Assert.Equal(3.338930, sdLog * sdLog, 4); // exhibit prints s^2 = 3.3389
        double ucl = LandExact.LandUcl(meanLog, sdLog, data.Length, 0.95);
        Assert.InRange(ucl, 2620, 2665);         // exhibit: 2658 (table lookup)
    }

    [Fact]
    public void Tg_example_2_2_matches_proucl_5_output()
    {
        double[] data = { 0.3489, 0.8526, 2.5445, 2.5602, 3.3706, 4.8911, 5.0930,
                          5.6408, 7.0407, 14.1715, 15.2608, 17.6214, 18.7690,
                          23.6804, 25.0461, 31.7720, 60.7066, 67.0926, 72.6243,
                          78.8357, 80.0867, 113.0230, 117.0360, 164.3302, 169.8303 };
        double meanLog = 0;
        foreach (double v in data) meanLog += Math.Log(v);
        meanLog /= data.Length;
        double ss = 0;
        foreach (double v in data) ss += Math.Pow(Math.Log(v) - meanLog, 2);
        double sdLog = Math.Sqrt(ss / (data.Length - 1));

        Assert.Equal(44.09, data.Average(), 1);  // TG: sample mean 44.09
        double ucl = LandExact.LandUcl(meanLog, sdLog, data.Length, 0.95);
        Assert.InRange(ucl, 229.0, 229.4);       // TG 5.1 (ProUCL 5.0): 229.2
    }

    /// <summary>
    /// Silver data (TG tables 2-3/2-4): ProUCL prints 18.54/11.11 by table
    /// interpolation; the exact values are lower. Statistics computed from the
    /// ProUCL 5.2 sample file silver-data.xls with all 56 observations treated
    /// as detected (as prescribed by TG example 2-1).
    /// </summary>
    [Fact]
    public void Silver_data_set_yields_exact_land_ucl()
    {
        // with outlier 560: n=56, meanlog 0.599920, sdlog 1.745840
        double uclWith = LandExact.LandUcl(0.599920, 1.745840, 56, 0.95);
        Assert.InRange(uclWith, 17.55, 17.65);   // exact; ProUCL (tables): 18.54

        // without outlier: n=55, meanlog 0.495774, sdlog 1.576629
        double uclWithout = LandExact.LandUcl(0.495774, 1.576629, 55, 0.95);
        Assert.InRange(uclWithout, 10.68, 10.76); // exact; ProUCL (tables): 11.11
    }

    [Fact]
    public void Quantile_and_cdf_are_inverse()
    {
        double nu = 20.0, zeta = 1.3;
        foreach (double p in new[] { 0.01, 0.05, 0.25, 0.5, 0.75, 0.95, 0.99 })
        {
            double q = LandExact.LandTQuantile(p, nu, zeta);
            Assert.InRange(LandExact.LandTCdf(q, nu, zeta), p - 1e-6, p + 1e-6);
        }
    }

    [Fact]
    public void Zero_zeta_reduces_to_central_student_t()
    {
        Assert.Equal(0.5, LandExact.LandTCdf(0.0, 15.0, 0.0), 8);
        double q = LandExact.LandTQuantile(0.95, 15.0, 0.0);
        double tCentral = StudentT.Quantile(0.95, 15);   // lower-tail convention
        Assert.InRange(q, tCentral - 1e-4, tCentral + 1e-4);
    }

    [Fact]
    public void Mild_case_is_reasonable_against_t_ucl()
    {
        // For s = 0.2, n = 30 (near-normal), Land's UCL must sit slightly above
        // the classical t UCL of the log-mean but far below the H explosion regime.
        double meanLog = 1.0, sdLog = 0.2;
        int n = 30;
        double land = LandExact.LandUcl(meanLog, sdLog, n, 0.95);
        double tUcl = Math.Exp(meanLog + sdLog * sdLog / 2.0
            + StudentT.Quantile(0.95, n - 1) * sdLog / Math.Sqrt(n));
        Assert.InRange(land, tUcl, tUcl * 1.2);
    }
}
