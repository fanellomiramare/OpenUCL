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
    public void ShapiroWilk_rejects_uniform_and_exponential_shaped_data()
    {
        // Uniform on 0..1, n=30: strongly non-normal.
        var uniform = Enumerable.Range(1, 30).Select(i => i / 31.0).ToArray();
        var swU = GoodnessOfFit.ShapiroWilk(uniform);
        Assert.True(swU.W < swU.CriticalValue(0.05), $"uniform W={swU.W} should be below critical");

        // Right-skewed sample (exponential-like): 1..30 is monotone; use squares for skew.
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

    // Regression pin on a fixed sample. True golden parity with ProUCL/R output is
    // deferred to the validation harness (docs: CONTRIBUTING.md); this test guards
    // against accidental algorithm changes in the meantime.
    [Fact]
    public void ShapiroWilk_regression_pin_n20()
    {
        double[] data = { 0.13, 0.19, 0.2, 0.22, 0.23, 0.28, 0.3, 0.33, 0.36, 0.38,
                          0.39, 0.4, 0.41, 0.43, 0.46, 0.48, 0.6, 0.63, 0.68, 0.97 };
        var sw = GoodnessOfFit.ShapiroWilk(data);
        Assert.Equal(0.886976, sw.W, 5);
        // Data are right-skewed (max 0.97 detached): must reject at 5%.
        Assert.True(sw.PValue < 0.05, $"p={sw.PValue}");
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
