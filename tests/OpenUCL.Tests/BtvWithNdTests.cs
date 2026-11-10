using OpenUCL.Core;
using OpenUCL.Data;
using Xunit;

namespace OpenUCL.Tests;

/// <summary>
/// BTV-ND via KM (TG 5.3): consistency checks against the validated KM estimates,
/// on the real Oahu data set when available.
/// </summary>
public class BtvWithNdTests
{
    private static IReadOnlyList<ValueObservation>? OahuObservations()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "ProUCL", "Data", "Oahu.xls");
        if (!File.Exists(path)) return null;
        var table = LongTableReader.ReadTable(path);
        var options = new LongTableReadOptions { NdFlagColumn = "D_Arsenic", NdFlagValue = "0" };
        var series = LongTableReader.ToSeries(table,
            new SeriesSelection { ValueColumns = new[] { "Arsenic" } }, options).ToList();
        return series[0].Observations;
    }

    [Fact]
    public void Upl_normal_km_consistent_with_km_estimates()
    {
        var obs = OahuObservations();
        if (obs is null) return; // CI: skip

        var km = Censored.KaplanMeier(obs);
        var b = BtvWithNd.Compute(obs, 0.95, 0.95, futureK: 5);

        Assert.Equal(km.Mean, b.KmMean, 12);
        Assert.Equal(km.Sd, b.KmSd, 12);

        double t = StudentT.Quantile(0.95, 23);
        double sqrt1p = Math.Sqrt(1 + 1 / 24.0);
        Assert.Equal(km.Mean + t * km.Sd * sqrt1p, b.UplNormal1, 10);          // TG eq. 5-1
        double tBonf = StudentT.Quantile(1 - 0.05 / 5, 23);
        Assert.Equal(km.Mean + tBonf * km.Sd * sqrt1p, b.UplNormalK, 10);
        Assert.Equal(km.Mean + t * km.Sd * Math.Sqrt(1 / 5.0 + 1 / 24.0), b.UplNormalKMean, 10);
        Assert.Equal(km.Mean + Math.Sqrt(1 / 0.05 - 1) * km.Sd * sqrt1p, b.UplChebyshev, 10);   // eq. 5-2
        double k = NoncentralT.ToleranceFactor(24, 0.95, 0.95);
        Assert.Equal(km.Mean + k * km.Sd, b.UtlNormal, 10);                    // TG eq. 5-4
        Assert.Equal(km.Mean + NormalDistribution.Quantile(0.95) * km.Sd, b.PercentileNormal, 10); // eq. 5-3
    }

    [Fact]
    public void Transformed_km_variants_are_finite_and_ordered()
    {
        var obs = OahuObservations();
        if (obs is null) return;

        var b = BtvWithNd.Compute(obs, 0.95, 0.95, futureK: 5);
        Assert.True(b.LognormalApplicable);
        Assert.True(b.GammaApplicable);
        Assert.InRange(b.UplLognormal1, 1.0, 6.0);
        Assert.InRange(b.UplGammaWh, 0.5, 6.0);
        Assert.InRange(b.UplGammaHw, 0.5, 6.0);
        Assert.InRange(b.PercentileGammaWh, 0.5, 5.0);
        Assert.InRange(b.UtlGammaWh, 0.5, 8.0);
        Assert.InRange(b.UtlLognormal, 0.5, 9.0);
    }

    [Fact]
    public void Synthetic_nd_set_is_processed()
    {
        // 8 detects + 4 NDs at three DLs (Oahu-like structure)
        double[] detects = { 0.9, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2.8, 3.2 };
        // build a 24-obs set: NDs at 0.5 (3x), 0.6, 0.7 (2x), 1.5, 1.7 + detects
        var obs = new List<ValueObservation>
        {
            ValueObservation.Nondetect(0.5), ValueObservation.Nondetect(0.5),
            ValueObservation.Nondetect(0.5), ValueObservation.Nondetect(0.6),
            ValueObservation.Nondetect(0.7), ValueObservation.Nondetect(0.7),
            ValueObservation.Detect(0.9), ValueObservation.Detect(1),
            ValueObservation.Detect(1), ValueObservation.Detect(1), ValueObservation.Detect(1),
            ValueObservation.Nondetect(1.5), ValueObservation.Nondetect(1.7),
            ValueObservation.Detect(2), ValueObservation.Detect(2), ValueObservation.Detect(2),
            ValueObservation.Detect(2), ValueObservation.Detect(2), ValueObservation.Detect(2),
            ValueObservation.Detect(2), ValueObservation.Nondetect(2.8),
            ValueObservation.Detect(3.2), ValueObservation.Detect(1.2),
            ValueObservation.Detect(1.1),
        };
        var b = BtvWithNd.Compute(obs, 0.95, 0.95, futureK: 5);

        Assert.Equal(24, b.N);
        Assert.Equal(9, b.Nondetects);
        Assert.InRange(b.KmMean, 0.5, 2.5);
        Assert.InRange(b.UplNormal1, b.KmMean, 4 * b.KmMean + 1);
        Assert.InRange(b.UtlNormal, b.UplNormal1, b.UplNormal1 + 2.0);   // UTL >= UPL typically
        Assert.True(b.LognormalApplicable);
        Assert.InRange(b.UplLognormal1, b.UplNormal1 * 0.5, b.UplNormal1 * 2.0);
        Assert.InRange(b.PercentileNormal, b.KmMean, 3.0 * b.KmMean);
    }
}
