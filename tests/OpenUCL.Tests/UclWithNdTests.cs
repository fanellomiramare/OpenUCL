using OpenUCL.Core;
using OpenUCL.Data;
using Xunit;

namespace OpenUCL.Tests;

public class UclWithNdTests
{
    private static ValueObservation D(double v) => ValueObservation.Detect(v);
    private static ValueObservation Nd(double dl) => ValueObservation.Nondetect(dl);

    [Fact]
    public void Km_t_ucl_consistent_with_km_estimates()
    {
        // Oahu data (when available): KM(t) = KM mean + t(0.95, 23) * KM SE.
        var obs = OahuObservations();
        if (obs is null) return; // dataset not present (CI): skip

        var un = UclWithNd.Compute(obs, 0.95, 2000, 381);
        var km = Censored.KaplanMeier(obs);
        double t = StudentT.Quantile(0.95, 23);

        Assert.Equal(km.Mean, un.KmMean, 12);
        Assert.Equal(km.Mean + t * km.StandardErrorOfMean, un.KmT, 12);
        Assert.Equal(km.Mean + Math.Sqrt(1.0 / 0.05 - 1.0) * km.StandardErrorOfMean, un.KmChebyshev, 12);

        // Bootstrap: reproducible with the same seed, KM mean near the estimate.
        var again = UclWithNd.Compute(obs, 0.95, 2000, 381);
        Assert.Equal(un.KmPercentileBootstrap, again.KmPercentileBootstrap, 12);
        Assert.InRange(un.KmPercentileBootstrap, 0.5 * km.Mean, 1.5 * km.Mean);
    }

    /// <summary>
    /// TG Example 4-4 (Oahu continued): "ProUCL suggests using GROS UCL of 1.27" —
    /// the gamma adjusted UCL computed on the GROS-imputed data set.
    /// </summary>
    [Fact]
    public void Oahu_gros_gamma_ucl_near_TG_value()
    {
        var obs = OahuObservations();
        if (obs is null) return;

        var un = UclWithNd.Compute(obs, 0.95, 2000, 381);
        Assert.False(double.IsNaN(un.GrosGammaAdjusted), "GROS gamma adjusted UCL should be computable on Oahu");
        Assert.InRange(un.GrosGammaAdjusted, 1.05, 1.50);   // TG suggestion: 1.27
    }

    [Fact]
    public void All_nondetects_throws()
    {
        Assert.Throws<ArgumentException>(
            () => UclWithNd.Compute(new List<ValueObservation> { Nd(1), Nd(2), Nd(3) }));
    }

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
}
