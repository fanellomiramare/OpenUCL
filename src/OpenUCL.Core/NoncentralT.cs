namespace OpenUCL.Core;

/// <summary>
/// Noncentral Student's t distribution: CDF via the Poisson-mixture of incomplete
/// beta functions (Benton &amp; Krishnamoorthy 2003 formulation). Used for the exact
/// tolerance factor K of one-sided normal tolerance limits (UTLs).
/// </summary>
public static class NoncentralT
{
    /// <summary>CDF P(T' &lt;= t) with df degrees of freedom and noncentrality delta.</summary>
    public static double Cdf(double t, double df, double delta)
    {
        if (df <= 0) throw new ArgumentOutOfRangeException(nameof(df));
        double lambda = 0.5 * delta * delta;
        double x = t * t / (t * t + df);
        double sum = 0, weight = Math.Exp(-lambda);
        // Poisson mixture; weights decay factorially — 200 terms is ample for |delta| < 40.
        for (int j = 0; j < 400; j++)
        {
            double term = t >= 0
                ? SpecialFunctions.BetaI(x, j + 0.5, df / 2.0)
                : 1.0 - SpecialFunctions.BetaI(x, df / 2.0, j + 0.5);
            sum += weight * term;
            if (weight < 1e-18) break;
            weight *= lambda / (j + 1.0);
        }
        return Math.Min(Math.Max(sum, 0.0), 1.0);
    }

    /// <summary>Quantile: P(T' &lt;= q) = p, p in (0,1).</summary>
    public static double Quantile(double p, double df, double delta)
    {
        if (p is <= 0.0 or >= 1.0)
            throw new ArgumentOutOfRangeException(nameof(p), "p must lie strictly between 0 and 1.");
        // The Poisson-mixture CDF underflows for very large df; there the
        // noncentral t is already N(delta, 1) for all practical purposes.
        if (df > 10000) return delta + NormalDistribution.Quantile(p);
        double lo = -100.0, hi = 100.0;
        while (Cdf(hi, df, delta) < p) hi *= 2.0;
        for (int i = 0; i < 200; i++)
        {
            if (hi - lo < 1e-15 * Math.Max(1.0, Math.Abs(hi))) break;
            double mid = 0.5 * (lo + hi);
            if (Cdf(mid, df, delta) < p) lo = mid;
            else hi = mid;
        }
        return 0.5 * (lo + hi);
    }

    /// <summary>
    /// Exact one-sided normal tolerance factor K(n, cc, coverage) for UTLs:
    /// K = t'(1-cc; n-1, z_coverage * sqrt(n)) / sqrt(n). Equals the Hahn-Meeker
    /// tabulated values (ProUCL uses tables for n &lt;= 30 and the Natrella
    /// approximation for larger n; the exact noncentral-t value is used here).
    /// </summary>
    public static double ToleranceFactor(int n, double cc, double coverage)
    {
        if (n < 2) throw new ArgumentOutOfRangeException(nameof(n));
        double delta = NormalDistribution.Quantile(coverage) * Math.Sqrt(n);
        return Quantile(cc, n - 1, delta) / Math.Sqrt(n);
    }
}
