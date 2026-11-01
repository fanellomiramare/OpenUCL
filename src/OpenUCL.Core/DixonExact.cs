namespace OpenUCL.Core;

/// <summary>
/// Exact critical values for Dixon's subrange outlier tests under normality.
/// The null distribution of the ratio R = (Z(c) - Z(b)) / (Z(c) - Z(a)) of normal
/// order statistics Z(1)&lt;=...&lt;=Z(n) is integrated numerically on the unit cube:
/// with (u, v, w) = (F(x_a), F(x_b), F(x_c)) the joint density of the three order
/// statistics becomes a polynomial in (u, v, w), and the integral over v is
/// closed-form via the incomplete beta function. Critical values solve
/// P(R &gt; c) = alpha by bisection. Exact for every n and alpha (ProUCL uses the
/// rounded historical tables of Rorabacher 1991 / Gilbert 1987).
/// </summary>
public static class DixonExact
{
    private static readonly Dictionary<int, (double[] Nodes, double[] Weights)> GaussCache = new();

    /// <summary>
    /// Exact critical value c with P(R &gt; c) = alpha for the subrange ratio defined
    /// by the 1-based order statistics (a, b, c) — e.g. r11 at n=10 is (2, 9, 10).
    /// Cached: the value depends only on (n, a, b, c, alpha).
    /// </summary>
    public static double CriticalValue(int n, int a, int b, int c, double alpha)
    {
        var key = (n, a, b, c, alpha);
        if (Cache.TryGetValue(key, out double cached)) return cached;

        double lo = 0.0, hi = 1.0;             // the ratio lies in (0, 1)
        for (int i = 0; i < 60; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (TailProbability(n, a, b, c, mid) > alpha) lo = mid;
            else hi = mid;
        }
        double value = 0.5 * (lo + hi);
        Cache[key] = value;
        return value;
    }

    private static readonly Dictionary<(int n, int a, int b, int c, double alpha), double> Cache = new();

    /// <summary>
    /// P(R &gt; crit) for the subrange ratio built on order statistics (a, b, c),
    /// 1-based with a &lt; b &lt; c. Normalization sanity check: TailProbability(0) = 1.
    /// </summary>
    public static double TailProbability(int n, int a, int b, int c, double crit)
    {
        // Constant of the joint order-statistic density on the unit cube:
        // n! / ((a-1)! (b-a-1)! (c-b-1)! (n-c)!)
        double constant = Math.Exp(SpecialFunctions.LogGamma(n + 1)
            - SpecialFunctions.LogGamma(a)
            - SpecialFunctions.LogGamma(b - a)
            - SpecialFunctions.LogGamma(c - b)
            - SpecialFunctions.LogGamma(n - c + 1));
        // B(b-a, c-b): normalization of the inner beta integral.
        double betaNorm = Math.Exp(SpecialFunctions.LogGamma(b - a)
            + SpecialFunctions.LogGamma(c - b)
            - SpecialFunctions.LogGamma(c - a));

        (double[] nodes, double[] weights) = Gauss(64);
        double sum = 0.0;
        for (int iw = 0; iw < nodes.Length; iw++)
        {
            double w = nodes[iw];                       // position of x(c) in probability scale
            double zw = NormalDistribution.Quantile(w);
            // inner integral over u in (0, w): Gauss-Legendre mapped to (0, w)
            double inner = 0.0;
            for (int iu = 0; iu < nodes.Length; iu++)
            {
                double um = w * nodes[iu];
                double du = w * weights[iu];

                // R > crit  <=>  z(v) < (1-crit) z(w) + crit z(u)
                double vLimit = Math.Min(w, NormalDistribution.Cdf(
                    (1.0 - crit) * zw + crit * NormalDistribution.Quantile(um)));
                if (vLimit <= um) continue;

                double tStar = Math.Clamp((vLimit - um) / (w - um), 0.0, 1.0);
                inner += du
                    * Math.Pow(um, a - 1)
                    * Math.Pow(1.0 - w, n - c)
                    * Math.Pow(w - um, c - a - 1)
                    * SpecialFunctions.BetaI(tStar, b - a, c - b);
            }
            sum += weights[iw] * inner;
        }
        return Math.Min(Math.Max(constant * betaNorm * sum, 0.0), 1.0);
    }

    /// <summary>Gauss-Legendre nodes/weights on [0, 1], cached per point count.</summary>
    private static (double[] Nodes, double[] Weights) Gauss(int points)
    {
        if (GaussCache.TryGetValue(points, out var cached)) return cached;
        var nodes = new double[points];
        var weights = new double[points];
        for (int i = 0; i < points; i++)
        {
            // Newton iteration for the i-th root of the Legendre polynomial P_m.
            double z = Math.Cos(Math.PI * (i + 0.75) / (points + 0.5));
            double dp = 0.0;
            for (int it = 0; it < 100; it++)
            {
                double p0 = 1.0, p1 = 0.0;
                for (int j = 0; j < points; j++)
                {
                    double p2 = p1;
                    p1 = p0;
                    p0 = ((2.0 * j + 1.0) * z * p1 - j * p2) / (j + 1.0);
                }
                dp = points * (z * p0 - p1) / (z * z - 1.0);
                double dz = p0 / dp;
                z -= dz;
                if (Math.Abs(dz) < 1e-15) break;
            }
            nodes[i] = 0.5 * (z + 1.0);              // map [-1, 1] -> [0, 1]
            weights[i] = 2.0 / ((1.0 - z * z) * dp * dp);  // on [-1, 1]
        }
        // halve the weights for the [0, 1] mapping
        for (int i = 0; i < points; i++) weights[i] *= 0.5;
        var result = (nodes, weights);
        GaussCache[points] = result;
        return result;
    }
}
