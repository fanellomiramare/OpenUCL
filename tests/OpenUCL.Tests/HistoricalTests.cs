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
    /// observations as detected, run 2026-11-11): 90% 15.61, 95% 19.12,
    /// 97.5% 24.00, 99% 33.59 — identical to TG Table 2-3. An earlier version
    /// passed the distribution sd instead of the SE, yielding ~126.8 at 95%.
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

        Assert.InRange(LognormalHistorical.ChebyshevMvueUcl(mvueMean, se, 0.90), 15.60, 15.62);
        Assert.InRange(LognormalHistorical.ChebyshevMvueUcl(mvueMean, se, 0.95), 19.11, 19.13);
        Assert.InRange(LognormalHistorical.ChebyshevMvueUcl(mvueMean, se, 0.975), 23.99, 24.01);
        Assert.InRange(LognormalHistorical.ChebyshevMvueUcl(mvueMean, se, 0.99), 33.58, 33.60);
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
