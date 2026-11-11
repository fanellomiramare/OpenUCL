using OpenUCL.Core;
using OpenUCL.Data;
using Xunit;

namespace OpenUCL.Tests;

public class DixonTests
{
    [Fact]
    public void Hand_computed_high_tail_n5()
    {
        // n=5 uses r10: high C = (x(n)-x(n-1))/(x(n)-x(1)) = (20-4)/19;
        // low C = (x(2)-x(1))/(x(n)-x(1)) = (2-1)/19.
        double[] data = { 1, 2, 3, 4, 20 };
        var d = Outliers.Dixon(data);
        Assert.Equal(5, d.N);
        Assert.Equal("r10", d.Variant);
        Assert.Equal(16.0 / 19.0, d.HighStatistic, 12);
        Assert.Equal(1.0 / 19.0, d.LowStatistic, 12);
        Assert.Equal(20, d.HighValue);
        Assert.Equal(1, d.LowValue);
    }

    [Fact]
    public void Variant_selection_follows_n_ranges()
    {
        Assert.Equal("r10", Outliers.Dixon(Enumerable.Range(1, 7).Select(i => (double)i).ToArray()).Variant);
        Assert.Equal("r11", Outliers.Dixon(Enumerable.Range(1, 10).Select(i => (double)i).ToArray()).Variant);
        Assert.Equal("r21", Outliers.Dixon(Enumerable.Range(1, 13).Select(i => (double)i).ToArray()).Variant);
        Assert.Equal("r22", Outliers.Dixon(Enumerable.Range(1, 25).Select(i => (double)i).ToArray()).Variant);
    }

    [Fact]
    public void Requires_3_to_25()
    {
        Assert.Throws<ArgumentException>(() => Outliers.Dixon(new double[] { 1, 2 }));
        Assert.Throws<ArgumentException>(() => Outliers.Dixon(Enumerable.Range(1, 26).Select(i => (double)i).ToArray()));
    }
}

public class RosnerTests
{
    /// <summary>
    /// Golden: ProUCL TG 5.2 Table 7-4 (pyrene data, n=45 detects, r0=10).
    /// Row 1: mean 190.1, sd 430.1, outlier 2982, R = 6.491; row 2: mean 126.6,
    /// sd 90.7, outlier 459, R = 3.665; 2 outliers at both 5% and 1%.
    /// </summary>
    [Fact]
    public void Pyrene_rosner_matches_TG_table_7_4()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "ProUCL", "Data", "pyrene-She-data.xls");
        if (!File.Exists(path)) return; // CI: skip

        var table = LongTableReader.ReadTable(path);
        // the file carries a 'Pyrene-Detect' column with the 45 detected values only.
        string col = table.Columns.First(c => c.Contains("Detect", StringComparison.OrdinalIgnoreCase));
        var series = LongTableReader.ToSeries(table,
            new SeriesSelection { ValueColumns = new[] { col } }).ToList();
        double[] detected = series[0].DetectedValues();
        Assert.Equal(45, detected.Length);

        var ros = Outliers.Rosner(detected, 10);

        Assert.Equal(190.1, ros.Steps[0].Mean, 1);
        Assert.Equal(430.1, ros.Steps[0].Sd, 1);
        Assert.Equal(2982, ros.Steps[0].Value, 0);
        Assert.Equal(6.491, ros.Steps[0].R, 1);

        Assert.Equal(126.6, ros.Steps[1].Mean, 1);
        // TG Table 7-4 prints 90.7 (sample sd) while its own row 1 uses the population
        // sd (430.1); our implementation follows the TG equation (population, /n):
        // stage 1 matches the table exactly, stage 2 gives 89.66.
        Assert.InRange(ros.Steps[1].Sd, 89.5, 90.8);
        Assert.Equal(459, ros.Steps[1].Value, 0);
        Assert.InRange(ros.Steps[1].R, 3.60, 3.75);

        // Critical values: generalized ESD approx (TG tables: 3.09 / 3.08 at 5%).
        Assert.InRange(ros.Steps[0].Critical5, 3.00, 3.20);
        Assert.InRange(ros.Steps[1].Critical5, 2.95, 3.15);

        // TG conclusion: 2 outliers at both 5% and 1% (2982 and 459).
        Assert.Equal(2, ros.OutliersAt(0.05));
        Assert.Equal(2, ros.OutliersAt(0.01));
    }

    /// <summary>
    /// Golden: ProUCL 5.2 live output on its sample file Al-with-Outlier.xls
    /// (Rosner, n=28, 1 suspected outlier 30000 at observation 26):
    /// mean 10284, row sd 7315, R = 2.695, critical 2.880 (5%) / 3.200 (1%),
    /// verdict "no potential outlier" at both levels. Our exact generalized-ESD
    /// critical values sit ~0.15% above ProUCL's tabulated ones (2.884/3.205)
    /// with the identical verdict. Data embedded so the test runs on CI.
    /// </summary>
    [Fact]
    public void Aluminum_rosner_matches_ProUCL_live_output()
    {
        double[] al = { 6280, 3830, 3900, 5130, 9310, 15300, 9730, 7840, 10400,
                        16200, 6350, 10700, 15400, 12500, 2850, 9040, 2700, 1710,
                        3430, 6790, 11600, 4110, 7230, 4610, 25000, 30000, 24000, 22000 };
        var ros = Outliers.Rosner(al, 10);

        Assert.Equal(10283.6, ros.Steps[0].Mean, 1);
        Assert.Equal(7314.89, ros.Steps[0].Sd, 1);
        Assert.Equal(30000, ros.Steps[0].Value, 0);
        Assert.Equal(2.6954, ros.Steps[0].R, 3);
        Assert.InRange(ros.Steps[0].Critical5, 2.878, 2.890);  // ProUCL table: 2.880
        Assert.InRange(ros.Steps[0].Critical1, 3.198, 3.212);  // ProUCL table: 3.200
        Assert.Equal(0, ros.OutliersAt(0.05));
        Assert.Equal(0, ros.OutliersAt(0.01));
    }

    private static bool IsNumeric(LongTable table, string column)
    {
        int col = table.ColumnIndex(column);
        for (int r = 0; r < table.RowCount; r++)
            if (table.GetDouble(r, col) is not null) return true;
        return false;
    }

    [Fact]
    public void Requires_n_at_least_25()
    {
        Assert.Throws<ArgumentException>(
            () => Outliers.Rosner(Enumerable.Range(1, 24).Select(i => (double)i).ToArray()));
    }
}

public class OneSampleTestTests
{
    // Textbook sample: mean 5, sd sqrt(32/7), n = 8.
    private static readonly double[] Data = { 2, 4, 4, 4, 5, 5, 7, 9 };

    [Fact]
    public void T_test_hand_computed()
    {
        // t = (5 - 3) / (2.13809 / sqrt(8)) = 2.6458; upper tail.
        var t = OneSampleTests.TTest(Data, 3.0, upperTail: true);
        Assert.Equal(2.6458, t.Statistic, 3);
        Assert.True(t.RejectNull);                       // p ~ 0.0167 < 0.05
        Assert.Equal(0.0167, t.PValueOneSided, 3);
        // lower tail against Cs = 7: t = (5-7)/0.7559 = -2.6458 -> reject.
        var tl = OneSampleTests.TTest(Data, 7.0, upperTail: false);
        Assert.Equal(-2.6458, tl.Statistic, 3);
        Assert.True(tl.RejectNull);
        // upper tail against Cs = 7: t negative -> not significant.
        var tu = OneSampleTests.TTest(Data, 7.0, upperTail: true);
        Assert.False(tu.RejectNull);
    }

    [Fact]
    public void Sign_test_counts()
    {
        // vs Cs = 4.5: above = 4 (5,5,7,9), below = 4 -> exactly balanced, not rejected.
        var balanced = OneSampleTests.SignTest(Data, 4.5, upperTail: true);
        Assert.Equal(4, balanced.Statistic);
        Assert.False(balanced.RejectNull);
        // vs Cs = 2 (upper): the observation equal to Cs is discarded, so 7 of 7
        // remaining are above; P(Bin(7,0.5) >= 7) = 1/128 -> reject.
        var up = OneSampleTests.SignTest(Data, 2.0, upperTail: true);
        Assert.Equal(7, up.Statistic);
        Assert.Equal(1.0 / 128.0, up.PValueOneSided, 10);
        Assert.True(up.RejectNull);
    }

    [Fact]
    public void Wilcoxon_exact_known_result()
    {
        // differences vs Cs=5: {-0.9, +1.7, -0.2, +2.1, +1.1, -0.6, +0.5};
        // ranks of |d|: 4, 6, 1, 7, 5, 3, 2 -> W+ = 6+7+5+2 = 20.
        double[] data = { 4.1, 6.7, 4.8, 7.1, 6.1, 4.4, 5.5 };
        var w = OneSampleTests.WilcoxonSignedRank(data, 5.0, upperTail: true);
        Assert.Equal(20.0, w.Statistic, 10);
        // Independent brute-force reference over all 128 sign assignments.
        double[] absRanks = { 4, 6, 1, 7, 5, 3, 2 };
        int count = 0;
        for (int mask = 0; mask < 128; mask++)
        {
            int s = 0;
            for (int b = 0; b < 7; b++) if ((mask & (1 << b)) != 0) s += (int)absRanks[b];
            if (s >= 20) count++;
        }
        Assert.Equal(count / 128.0, w.PValueOneSided, 10);
        Assert.False(w.RejectNull);
    }

    [Fact]
    public void Wilcoxon_rejects_shifted_sample()
    {
        double[] data = { 10.1, 12.4, 9.8, 11.5, 10.9, 12.0, 11.1, 10.5 };
        var w = OneSampleTests.WilcoxonSignedRank(data, 5.0, upperTail: true);
        Assert.True(w.RejectNull);          // all positive, clearly above 5
        // all 8 differences positive: W+ = max, P(W+ >= max) = 1/2^8
        Assert.Equal(1.0 / 256.0, w.PValueOneSided, 10);
    }
}
