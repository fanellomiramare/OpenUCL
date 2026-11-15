using OpenUCL.Core;
using Xunit;

namespace OpenUCL.Tests;

public class FinneyAndMvueTests
{
    /// <summary>
    /// MVUE consistency: for a large sample the MVUE mean converges to the
    /// approximate MLE mean exp(meanlog + s2/2).
    /// </summary>
    [Fact]
    public void Mvue_converges_to_approximate_for_large_n()
    {
        double meanLog = 0.6, sdLog = 1.746, n = 500;
        var (mvueMean, _) = LognormalHistorical.Mvue(meanLog, sdLog, (int)n);
        double approxMean = Math.Exp(meanLog + sdLog * sdLog / 2.0);
        Assert.InRange(mvueMean, approxMean * 0.99, approxMean * 1.005);
    }

    /// <summary>
    /// Chebyshev (MVUE) UCL (TG eq. 2-47): mu1 + SE(mu1)·sqrt((1-alpha)/alpha),
    /// where SE(mu1) is the standard error of the MVUE mean from TG eq. 2-16
    /// (Bradu-Mundlak) — NOT the MVUE of the distribution sd (eq. 2-15).
    /// Golden: ProUCL 5.2 live output sheet on silver-data.xls (all 56
    /// observations as detected, run 2026-11-11, Full Precision OFF): 90% 15.61,
    /// 95% 19.12, 97.5% 24.00, 99% 33.59 — identical to TG Table 2-3. Re-confirmed
    /// 2026-11-15 with Full Precision ON (campaign 2, scheda F) to 7 decimals:
    /// 15.605606 / 19.121108 / 24.000490 / 33.585089 — exact parity with our
    /// eq. 2-16 SE implementation. An earlier version passed the distribution
    /// sd instead of the SE, yielding ~126.8 at 95%.
    /// </summary>
    [Fact]
    public void Chebyshev_mvue_on_silver_matches_ProUCL_live_sheet()
    {
        // silver with outlier 560: n=56, meanlog 0.599920, sdlog 1.745840
        double meanLog = 0.599920, sdLog = 1.745840;
        int n = 56;
        var (mvueMean, _) = LognormalHistorical.Mvue(meanLog, sdLog, n);
        double se = LognormalHistorical.MvueSe(meanLog, sdLog, n);

        Assert.Equal(7.84454, mvueMean, 4);
        Assert.Equal(2.58702, se, 4);

        Assert.Equal(15.605606, LognormalHistorical.ChebyshevMvueUcl(mvueMean, se, 0.90), 4);
        Assert.Equal(19.121108, LognormalHistorical.ChebyshevMvueUcl(mvueMean, se, 0.95), 4);
        Assert.Equal(24.000490, LognormalHistorical.ChebyshevMvueUcl(mvueMean, se, 0.975), 4);
        Assert.Equal(33.585089, LognormalHistorical.ChebyshevMvueUcl(mvueMean, se, 0.99), 4);
    }

    /// <summary>
    /// Chebyshev (MVUE) on the silver data set WITHOUT the outlier 560
    /// (n=55, meanlog 0.495774, sdlog 1.576629): ProUCL 5.2 live sheet
    /// (2026-11-11): 90% 10.13, 95% 12.26, 97.5% 15.22, 99% 21.04 —
    /// identical to TG Table 2-4 (12.26 at 95%). Second independent
    /// confirmation of the SE-based formula.
    /// </summary>
    [Fact]
    public void Chebyshev_mvue_on_silver_no_outlier_matches_ProUCL_live_sheet()
    {
        double meanLog = 0.495774, sdLog = 1.576629;
        int n = 55;
        var (mvueMean, _) = LognormalHistorical.Mvue(meanLog, sdLog, n);
        double se = LognormalHistorical.MvueSe(meanLog, sdLog, n);

        Assert.Equal(5.424097, mvueMean, 5);
        Assert.Equal(1.569258, se, 5);

        Assert.InRange(LognormalHistorical.ChebyshevMvueUcl(mvueMean, se, 0.90), 10.12, 10.14);
        Assert.InRange(LognormalHistorical.ChebyshevMvueUcl(mvueMean, se, 0.95), 12.25, 12.27);
        Assert.InRange(LognormalHistorical.ChebyshevMvueUcl(mvueMean, se, 0.975), 15.21, 15.23);
        Assert.InRange(LognormalHistorical.ChebyshevMvueUcl(mvueMean, se, 0.99), 21.03, 21.05);
    }
}

public class HallBootstrapTests
{
    [Fact]
    public void Reproducible_with_same_seed()
    {
        double[] data = { 2, 4, 4, 4, 5, 5, 7, 9, 11, 14 };
        var a = LognormalHistorical.HallBootstrap(data, 0.95, 500, 381);
        var b = LognormalHistorical.HallBootstrap(data, 0.95, 500, 381);
        Assert.Equal(a, b, 12);
    }

    [Fact]
    public void Hall_ucl_above_mean_for_symmetric_data()
    {
        double[] data = { 2, 4, 4, 4, 5, 5, 7, 9, 11, 14 };
        var hall = LognormalHistorical.HallBootstrap(data, 0.95, 1000, 381);
        double mean = data.Average();
        Assert.InRange(hall, mean, mean + 4.0);
    }
}
