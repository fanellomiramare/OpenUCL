namespace OpenUCL.Core;

/// <summary>
/// Student's t distribution: CDF via the regularized incomplete beta function
/// (continued fraction, standard algorithm), quantile by bisection inversion.
/// </summary>
public static class StudentT
{
    /// <summary>CDF P(T <= t) with df degrees of freedom, df >= 1.</summary>
    public static double Cdf(double t, double df)
    {
        if (df <= 0) throw new ArgumentOutOfRangeException(nameof(df));
        double x = df / (df + t * t);
        double beta = SpecialFunctions.BetaI(x, 0.5 * df, 0.5);
        return t > 0 ? 1.0 - 0.5 * beta : 0.5 * beta;
    }

    /// <summary>Quantile: P(T <= q) = p, p in (0,1).</summary>
    public static double Quantile(double p, double df)
    {
        if (p is <= 0.0 or >= 1.0)
            throw new ArgumentOutOfRangeException(nameof(p), "p must lie strictly between 0 and 1.");
        if (p == 0.5) return 0.0;   // symmetric distribution
        double lo = -40.0, hi = 40.0;
        while (Cdf(hi, df) < p) hi *= 2.0;
        for (int i = 0; i < 200; i++)
        {
            if (hi - lo < 1e-15 * Math.Max(1.0, Math.Abs(hi))) break;
            double mid = 0.5 * (lo + hi);
            if (Cdf(mid, df) < p) lo = mid;
            else hi = mid;
        }
        return 0.5 * (lo + hi);
    }
}
