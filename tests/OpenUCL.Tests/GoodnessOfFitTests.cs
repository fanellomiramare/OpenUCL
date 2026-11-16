using OpenUCL.Core;
using Xunit;

namespace OpenUCL.Tests;

public class GoodnessOfFitTests
{
    // Deterministic "perfect normal" sample: exact normal quantiles -> W very close to 1.
    private static double[] PerfectNormal(int n) =>
        Enumerable.Range(1, n).Select(i => NormalDistribution.Quantile((i - 0.5) / n)).ToArray();

    [Fact]
    public void ShapiroWilk_on_exact_normal_scores_passes()
    {
        // Note: W on *exact* normal scores is close to, but below, 1 (the weight norm
        // is not exactly 1 at small n); it must comfortably pass the test.
        foreach (int n in new[] { 5, 10, 30, 100 })
        {
            var sw = GoodnessOfFit.ShapiroWilk(PerfectNormal(n));
            Assert.True(sw.W > 0.9, $"n={n}: W={sw.W}");
            Assert.True(sw.PValue > 0.3, $"n={n}: p={sw.PValue}");
        }
    }

    [Fact]
    public void ShapiroWilk_rejects_bimodal_and_right_skewed_data()
    {
        // Uniform ramp on 0..1, n=30: canonical SW does NOT reject it (R:
        // W = 0.9574506, p = 0.2662) — SW is weak on symmetric non-normal
        // alternatives. Assert the canonical do-not-reject behavior.
        var uniform = Enumerable.Range(1, 30).Select(i => i / 31.0).ToArray();
        var swU = GoodnessOfFit.ShapiroWilk(uniform);
        Assert.True(swU.W >= swU.CriticalValue(0.05), $"uniform W={swU.W} should be at/above critical");

        // Bimodal (two clusters at 1 and 10): strongly rejected (R: W = 0.6382368,
        // p = 2.2e-07).
        var bimodal = Enumerable.Repeat(1.0, 15).Concat(Enumerable.Repeat(10.0, 15)).ToArray();
        var swB = GoodnessOfFit.ShapiroWilk(bimodal);
        Assert.Equal(0.6382368, swB.W, 5);
        Assert.True(swB.PValue < 1e-5, $"bimodal p={swB.PValue}");

        // Right-skewed sample (exponential-like): squares 1..30 / 900 — rejected
        // at 5% (R: W = 0.9014205, p = 0.0091).
        var skewed = Enumerable.Range(1, 30).Select(i => i * i / 900.0).ToArray();
        var swS = GoodnessOfFit.ShapiroWilk(skewed);
        Assert.True(swS.PValue < 0.05, $"skewed p={swS.PValue} should be < 0.05");
    }

    [Fact]
    public void ShapiroWilk_n3_exact_case()
    {
        // n=3 uses the exact closed-form branch of Royston (1995).
        // Data (0,1,2): perfectly symmetric -> W = 1, p = 1.
        var symmetric = GoodnessOfFit.ShapiroWilk(new double[] { 0, 1, 2 });
        Assert.Equal(1.0, symmetric.W, 12);
        Assert.Equal(1.0, symmetric.PValue, 12);

        // Data (0,1,3): mean 4/3, ss = 42/9; cross = sqrt(1/2)*3 -> W = (9/2)/(42/9) = 27/28.
        // p = (6/pi)(asin(sqrt(27/28)) - asin(sqrt(3/4))) = 0.6369 (hand-computed).
        var skewed = GoodnessOfFit.ShapiroWilk(new double[] { 0, 1, 3 });
        Assert.Equal(27.0 / 28.0, skewed.W, 12);
        Assert.Equal(0.6369, skewed.PValue, 3);
    }

    // Regression pin on a fixed sample. History: the 2026-11-16 investigation
    // (decision #77) found our W off vs the canonical AS R94 — the fac
    // normalization subtracted the poly-adjusted weights instead of the
    // original Blom weights; after the fix our W matches R 4.6.2 and
    // scipy 1.17.1 exactly on every dataset (see the referee golden below).
    // ProUCL's own W deviates from AS R94 in a different direction (e.g.
    // silver n=56: ProUCL 0.2058465 vs canonical 0.1835390); conclusions
    // agree at the sheet significance levels. This test guards against
    // accidental algorithm changes.
    [Fact]
    public void ShapiroWilk_regression_pin_n20()
    {
        double[] data = { 0.13, 0.19, 0.2, 0.22, 0.23, 0.28, 0.3, 0.33, 0.36, 0.38,
                          0.39, 0.4, 0.41, 0.43, 0.46, 0.48, 0.6, 0.63, 0.68, 0.97 };
        var sw = GoodnessOfFit.ShapiroWilk(data);
        Assert.Equal(0.913322, sw.W, 5);
        // Mildly right-skewed sample: the canonical algorithm does NOT reject
        // it at 5% (scipy: p = 0.0737571 — identical to ours, W and p).
        Assert.Equal(0.0737571, sw.PValue, 5);
    }

    /// <summary>
    /// Referee golden: our W is the canonical AS R94 — verified identical to
    /// R 4.6.2 shapiro.test and scipy 1.17.1 stats.shapiro on five data sets
    /// (2026-11-16, decision #77): mtcars mpg n=32 (0.9695029), silver raw
    /// n=56 (0.1835390), silver logged (0.9527821), silver without the 560
    /// outlier n=55 (0.4029366), and the (0,1,2,100) n=4 probe (0.64550176).
    /// ProUCL 5.2's own W deviates from the canonical algorithm in a
    /// different direction than our pre-fix port did (e.g. silver: ProUCL
    /// 0.2058465); documented deviation, conclusions agree.
    /// </summary>
    [Fact]
    public void ShapiroWilk_matches_canonical_AS_R94_referee()
    {
        var mtcars = new double[] { 21, 21, 22.8, 21.4, 18.7, 18.1, 14.3, 24.4, 22.8, 19.2, 17.8, 16.4, 17.3, 15.2, 10.4, 10.4, 14.7, 32.4, 30.4, 33.9, 21.5, 27.3, 26, 30.4, 15.8, 19.7, 15, 21.4, 21, 22.8, 24.4, 22.8 };
        Assert.Equal(0.9695029, GoodnessOfFit.ShapiroWilk(mtcars).W, 5);       // R/scipy: 0.9695029
        Assert.Equal(0.1835390, GoodnessOfFit.ShapiroWilk(BtvUnitTests.Silver).W, 5);   // R/scipy: 0.1835390
        Assert.Equal(0.9527821, GoodnessOfFit.ShapiroWilk(BtvUnitTests.Silver.Select(v => Math.Log(v)).ToArray()).W, 5); // R/scipy: 0.9527821
        Assert.Equal(0.4029366, GoodnessOfFit.ShapiroWilk(BtvUnitTests.Silver.Where(v => v != 560).ToArray()).W, 5); // R/scipy: 0.4029366
        Assert.Equal(0.64550176, GoodnessOfFit.ShapiroWilk(new double[] { 0, 1, 2, 100 }).W, 5); // R/scipy: 0.64550176
    }

    [Fact]
    public void CriticalValue_equals_W_where_pvalue_hits_alpha()
    {
        double[] data = PerfectNormal(12);
        var sw = GoodnessOfFit.ShapiroWilk(data);
        double alpha = 0.10;
        double critical = sw.CriticalValue(alpha);
        // Monotonicity of the p-value in W: W == critical <=> p == alpha.
        Assert.True((sw.PValue - alpha) * (sw.W - critical) > 0 || Math.Abs(sw.PValue - alpha) < 1e-12,
            $"p={sw.PValue}, alpha={alpha}, W={sw.W}, crit={critical}");
    }

    [Fact]
    public void Lilliefors_known_small_sample()
    {
        // n=3, data 0,1,2: mean 1, sample sd (n-1) = 1, z = (-1, 0, 1);
        // Phi = (0.158655, 0.5, 0.841345); D+ = D- = 1/3 - 0.158655 = 0.174678 (hand-computed).
        var lf = GoodnessOfFit.Lilliefors(new double[] { 0, 1, 2 });
        Assert.Equal(0.17468, lf.D, 4);
    }

    [Fact]
    public void Lilliefors_critical_value_formula()
    {
        var lf = GoodnessOfFit.Lilliefors(PerfectNormal(100));
        double crit95 = lf.CriticalValue(0.05);
        Assert.Equal(0.895 / ((0.83 + 100) / 10.0 - 0.01), crit95, 12);
        Assert.True(lf.D <= crit95, "perfect-normal sample must pass Lilliefors at 5%");
    }

    [Fact]
    public void Conclude_combines_tests_proUCL_style()
    {
        double[] data = PerfectNormal(40);
        var sw = GoodnessOfFit.ShapiroWilk(data);
        var lf = GoodnessOfFit.Lilliefors(data);
        Assert.Equal(GofConclusion.Normal, GoodnessOfFit.Conclude(sw, lf, 0.05));
    }

    [Fact]
    public void ShapiroWilk_requires_minimum_sample()
    {
        Assert.Throws<ArgumentException>(() => GoodnessOfFit.ShapiroWilk(new double[] { 1, 2 }));
        Assert.Throws<ArgumentException>(() => GoodnessOfFit.Lilliefors(new double[] { 1, 2 }));
    }
}
