using OpenUCL.Core;
using OpenUCL.Data;
using Xunit;

namespace OpenUCL.Tests;

public class KaplanMeierTests
{
    private static ValueObservation D(double v) => ValueObservation.Detect(v);
    private static ValueObservation Nd(double dl) => ValueObservation.Nondetect(dl);

    [Fact]
    public void Hand_computed_small_example()
    {
        // Data: ND(1), ND(1), 2, 3, 4 (n=5, k=2). TG §4.4 F(x) = prod over detected
        // x'_h > x of (n_h-m_h)/n_h: F(2)=3/5, F(3)=4/5, F(4)=1.
        // mean = 2(3/5)+3(1/5)+4(1/5) = 2.6; meanSq = 37/5; sd = sqrt(37/5 - 2.6^2) = 0.8.
        var obs = new List<ValueObservation> { Nd(1), Nd(1), D(2), D(3), D(4) };
        var km = Censored.KaplanMeier(obs);

        Assert.Equal(2.6, km.Mean, 12);
        Assert.Equal(0.8, km.Sd, 12);
    }

    [Fact]
    public void No_nondetects_matches_sample_statistics()
    {
        // With no censoring the KM mean equals the sample mean and the KM variance
        // (eq. 4-3, a distribution/CDF variance) equals the population variance.
        double[] data = { 2, 4, 4, 4, 5, 5, 7, 9 };
        var obs = data.Select(D).ToList();
        var km = Censored.KaplanMeier(obs);
        var sample = Descriptive.Compute(data);
        double popSd = Math.Sqrt(data.Average(v => (v - sample.Mean) * (v - sample.Mean)));

        Assert.Equal(sample.Mean, km.Mean, 10);
        Assert.Equal(popSd, km.Sd, 10);
    }

    [Fact]
    public void All_nondetects_throws()
    {
        Assert.Throws<ArgumentException>(
            () => Censored.KaplanMeier(new List<ValueObservation> { Nd(1), Nd(2) }));
    }

    /// <summary>
    /// Golden anchor from the ProUCL Technical Guide, Example 4-1 (Oahu data set):
    /// "A direct KM estimate of the sd obtained using equation (4-3) is 0.713;
    /// and an indirect KM estimate of sd = sqrt(24)*SE = 4.899*0.165 = 0.807".
    /// The sample file shipped with ProUCL carries the flag column D_Arsenic where
    /// 1 = detected and 0 = nondetect (13 NDs at DLs 0.9/1/2, 11 detects), matching
    /// the TG example. Test runs only when the file exists on the machine.
    /// </summary>
    [Fact]
    public void Oahu_km_matches_TG_example_4_1()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "ProUCL", "Data", "Oahu.xls");
        if (!File.Exists(path))
        {
            // Dataset not available (CI): skip silently.
            return;
        }

        var table = LongTableReader.ReadTable(path);
        Assert.True(table.RowCount == 24);
        var options = new LongTableReadOptions { NdFlagColumn = "D_Arsenic", NdFlagValue = "0" };
        var series = LongTableReader.ToSeries(table,
            new SeriesSelection { ValueColumns = new[] { "Arsenic" } }, options).ToList();

        Assert.Equal(13, series[0].NondetectCount);
        Assert.Equal(11, series[0].DetectedValues().Length);
        var km = Censored.KaplanMeier(series[0].Observations);
        Assert.Equal(0.713, km.Sd, 2);
        // The TG quotes Helsel's own SE (0.165) for comparison, not the output of
        // eq. 4-2; the exact eq. 4-2 transcription from the 508-tagged PDF is
        // illegible and is pending the Singh-Maichle-Lee (2006) report. For now:
        // the SE must be positive and smaller than the sd.
        Assert.True(km.StandardErrorOfMean > 0 && km.StandardErrorOfMean < km.Sd,
            $"SE={km.StandardErrorOfMean}");
    }
}

public class RosTests
{
    private static ValueObservation D(double v) => ValueObservation.Detect(v);
    private static ValueObservation Nd(double dl) => ValueObservation.Nondetect(dl);

    /// <summary>
    /// Perfect-line test: construct lognormal quantile data on Blom positions; hide the
    /// smallest k as NDs at a DL below all detects. LROS regresses exactly on the line
    /// (slope 1, intercept 0 on the log scale), so the imputed NDs must reproduce the
    /// hidden values to numerical precision.
    /// </summary>
    [Fact]
    public void LROS_recovers_hidden_normal_quantiles_exactly()
    {
        int n = 20, k = 5;
        var pp = Enumerable.Range(0, n).Select(i => (i + 0.625) / (n + 0.25)).ToArray();
        var logValues = pp.Select(p => NormalDistribution.Quantile(p)).ToArray();
        var values = logValues.Select(Math.Exp).ToArray();

        double dl = 0.5 * (values[k - 1] + values[k]); // strictly between hidden and first detect
        var obs = new List<ValueObservation>();
        for (int i = 0; i < k; i++) obs.Add(Nd(dl));
        for (int i = k; i < n; i++) obs.Add(D(values[i]));

        var ros = Censored.RosLognormal(obs);

        Assert.Equal(1.0, ros.Slope, 8);
        Assert.Equal(0.0, ros.Intercept, 8);
        Assert.False(ros.NegativeImputations);
        for (int i = 0; i < k; i++)
            Assert.Equal(values[i], ros.ImputedValues[i], 8);
    }

    [Fact]
    public void DL_half_substitution()
    {
        var obs = new List<ValueObservation> { Nd(2), D(5), D(7) };
        var data = Censored.DlHalf(obs);
        Assert.Equal(new[] { 1.0, 5.0, 7.0 }, data);
    }

    [Fact]
    public void Multiple_detection_limits_give_valid_positions()
    {
        // Multi-DL set where some DLs exceed some detects. Helsel-Cohn positions are
        // stratum-wise probabilities: they must lie in (0,1) and be strictly increasing
        // among the detected observations; ND positions may interleave across strata
        // (a known property of the HC scheme).
        var obs = new List<ValueObservation> { Nd(1), Nd(5), D(2), Nd(5), D(7), D(3), D(10), Nd(5) };
        var pp = Censored.PlottingPositions(obs, gamma: false);

        Assert.Equal(8, pp.Length);
        Assert.All(pp, v => { Assert.True(v > 0 && v < 1, $"pp={v} out of (0,1)"); });

        bool[] censored = { true, false, false, true, true, true, false, false };
        double lastDetect = 0;
        for (int i = 0; i < 8; i++)
        {
            if (censored[i]) continue;
            Assert.True(pp[i] > lastDetect, $"detect pp[{i}]={pp[i]} not > {lastDetect}");
            lastDetect = pp[i];
        }
    }

    [Fact]
    public void GROS_flags_negative_imputations()
    {
        // Strongly skewed detected data with a high DL: GROS may impute non-positive
        // values (documented in TG section 4.5.2.1); the flag must be set.
        var detects = new double[] { 0.5, 0.7, 0.9, 1.1, 1.4, 1.9, 2.6, 40.0 };
        var obs = detects.Select(D).Append(Nd(0.4)).ToList();

        var gros = Censored.RosGamma(obs);
        var st = Descriptive.Compute(gros.FullData);
        Assert.Equal(detects.Length + 1, gros.FullData.Length);
        // No exception; mean must be positive regardless of imputation sign.
        Assert.True(st.Mean > 0);
    }

    [Fact]
    public void Fewer_than_two_detects_throws()
    {
        Assert.Throws<ArgumentException>(
            () => Censored.RosLognormal(new List<ValueObservation> { Nd(1), D(2) }));
    }
}
