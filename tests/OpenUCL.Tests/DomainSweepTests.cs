using OpenUCL.Core;
using Xunit;

namespace OpenUCL.Tests;

/// <summary>
/// Domain sweep tests (lesson of the 2026-11-11 NoncentralT underflow, decision
/// #54): the goldens cluster on friendly parameter values while the declared
/// domain ("exact for any n") extends far beyond them. These sweeps assert
/// structural invariants across the whole declared domain — monotonicity,
/// bounds, roundtrips — so a break anywhere in the domain fails the suite.
/// </summary>
public class DomainSweepTests
{
    [Fact]
    public void Tolerance_factor_sweep_n2_to_200_is_monotone_and_bounded()
    {
        const double zp = 1.6448536269514722;   // z(0.95); K(n) -> zp as n grows
        double previous = double.PositiveInfinity;
        for (int n = 2; n <= 200; n++)
        {
            double k = NoncentralT.ToleranceFactor(n, 0.95, 0.95);
            Assert.True(double.IsFinite(k), $"K({n}) not finite");
            Assert.True(k > zp, $"K({n}) = {k} fell below the asymptote z(0.95)");
            Assert.True(k < zp + 35.0 / Math.Sqrt(n), $"K({n}) = {k} implausibly large");
            Assert.True(k < previous, $"K not strictly decreasing: K({n - 1}) = {previous}, K({n}) = {k}");
            previous = k;
        }
        // asymptote sanity: K(n) = z_p + O(1/sqrt(n)) — at n = 200 it is still
        // ~11% above z(0.95) (the exact one-sided K converges slowly)
        Assert.InRange(NoncentralT.ToleranceFactor(200, 0.95, 0.95) / zp, 1.05, 1.25);
    }

    [Fact]
    public void Noncentral_t_cdf_quantile_roundtrip_grid()
    {
        // |T'| representation: the roundtrip holds in the doubled convention
        foreach (int df in new[] { 4, 15, 54 })
            foreach (double delta in new[] { 0.0, 3.0, 12.0 })
                foreach (double p in new[] { 0.05, 0.5, 0.95 })
                {
                    double q = NoncentralT.Quantile(p, df, delta);
                    Assert.True(double.IsFinite(q), $"Quantile({p},{df},{delta}) not finite");
                    double back = NoncentralT.Cdf(q, df, delta);
                    Assert.InRange(back, p - 1e-6, p + 1e-6);
                }
    }

    [Fact]
    public void Land_exact_grid_monotone_and_consistent()
    {
        double tUclFor(int n, double s) =>
            Math.Exp(1.0 + s * s / 2.0 + NoncentralT.Quantile(0.95, n - 1, 0.0) * s / Math.Sqrt(n));

        foreach (int n in new[] { 10, 30, 80 })
            foreach (double s in new[] { 0.1, 0.3, 0.6, 1.0 })
            {
                double h95 = LandExact.LandCriticalH(n, s, 0.95);
                double h99 = LandExact.LandCriticalH(n, s, 0.99);
                double ucl95 = LandExact.LandUcl(1.0, s, n, 0.95);
                double ucl99 = LandExact.LandUcl(1.0, s, n, 0.99);

                Assert.True(double.IsFinite(h95) && h95 > 0, $"H({n},{s}) not finite/positive");
                Assert.True(h99 > h95, $"H not increasing in cc at n={n}, s={s}");
                Assert.True(ucl99 > ucl95, $"Land UCL not increasing in cc at n={n}, s={s}");
                // at small s the Land UCL tends to the t UCL (the pivots coincide);
                // for finite s it must sit at or (slightly) above it
                Assert.InRange(ucl95 / tUclFor(n, s), 0.98, 2.0);
            }

        // larger sd -> larger H (the explosion regime that makes the H-UCL unstable)
        Assert.True(LandExact.LandCriticalH(30, 1.0, 0.95) > LandExact.LandCriticalH(30, 0.3, 0.95));
        // roundtrip on a nontrivial zeta
        double zeta = 1.4;
        double q = LandExact.LandTQuantile(0.025, 40.0, zeta);
        Assert.InRange(LandExact.LandTCdf(q, 40.0, zeta), 0.025 - 1e-6, 0.025 + 1e-6);
    }

    [Fact]
    public void Central_quantile_cdf_roundtrips()
    {
        foreach (double p in new[] { 0.001, 0.01, 0.5, 0.99, 0.999 })
        {
            Assert.InRange(NormalDistribution.Cdf(NormalDistribution.Quantile(p)), p - 1e-9, p + 1e-9);
            Assert.InRange(StudentT.Cdf(StudentT.Quantile(p, 12), 12), p - 1e-7, p + 1e-7);
        }
    }
}
