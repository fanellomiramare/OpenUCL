namespace OpenUCL.Core;

/// <summary>
/// Historical lognormal methods (ProUCL Technical Guide 5.2 chapter 2):
/// MVUE of the lognormal mean/sd via Finney's function (Bradu-Mundlak series,
/// TG eq. 2-14/2-15), Chebyshev UCL on the MVUEs (eq. 2-47) and Hall's
/// skewness-adjusted bootstrap (eq. 2-63/2-64).
/// Land's H-UCL is planned separately (requires Land's H tables or the polar
/// integration of Land's conditional t distribution).
/// </summary>
public static class LognormalHistorical
{
    /// <summary>
    /// Finney's function g(m, z) (Finney 1941; Bradu-Mundlak 1970 series):
    /// g = sum of terms 1, m·z/(m+1), m^(2p) z^p (m+2p) / (m · (m+2)···(m+2p) · (m+1)^p · p!)
    /// for p = 2.., summed until the last computed term is below the double-epsilon
    /// tolerance (EnvStats finneys.g.scalar algorithm, z may be negative).
    /// </summary>
    public static double FinneysG(int m, double z, int nTermsInc = 10)
    {
        if (m < 1) throw new ArgumentOutOfRangeException(nameof(m));
        for (int iter = 1; iter <= 100; iter++)
        {
            int nTerms = nTermsInc * iter;
            var terms = FinneyTerms(m, z, nTerms);
            if (Math.Abs(terms[^1]) <= double.Epsilon)
                return terms.Sum();
        }
        return FinneyTerms(m, z, nTermsInc * 100).Sum();
    }

    /// <summary>
    /// Series terms of Finney's g-function in log space (port of the EnvStats
    /// finneys.g.scalar.sub): vec[0] = 1; vec[1] = m·z/(m+1);
    /// vec[p+1] (p >= 2) = m^(2p)·(m+2p)·z^p / (m·(m+2)···(m+2p)·(m+1)^p·p!)
    /// with alternating signs when z &lt; 0.
    /// </summary>
    private static List<double> FinneyTerms(int m, double z, int nTerms)
    {
        var terms = new List<double>(nTerms) { 1.0 };
        if (nTerms > 2)
        {
            // term 1: m·z/(m+1)
            terms.Add(m * z / (m + 1.0));
            double cumL = 0.0, cumK = 0.0;
            for (int p = 2; p <= nTerms - 1; p++)
            {
                double num = 2.0 * p * Math.Log(m)
                    + Math.Log(m + 2.0 * p)
                    + p * Math.Log(Math.Abs(z));
                cumL += Math.Log(m + 2.0 * p);
                cumK += Math.Log(p);
                double den = Math.Log(m) + Math.Log(m + 2.0) + cumL
                    + p * Math.Log(m + 1.0) + cumK;
                double sign = z > 0 ? 1.0 : Math.Pow(-1.0, p + 1);
                terms.Add(Math.Exp(num - den) * sign);
            }
        }
        return terms;
    }

    /// <summary>
    /// MVUE of the lognormal mean and sd (TG eq. 2-14/2-15):
    /// mu1 = exp(meanlog) * finneys.g(n-1, s2/2);
    /// sd1 = sqrt(exp(2*meanlog) * (g(n-1, 2*s2) - g(n-1, s2*(n-2)/(n-1)))).
    /// </summary>
    public static (double MvueMean, double MvueSd) Mvue(double meanLog, double sdLog, int n)
    {
        if (n < 2) throw new ArgumentOutOfRangeException(nameof(n));
        double s2 = sdLog * sdLog;
        double gMean = FinneysG(n - 1, s2 / 2.0);
        double mvueMean = Math.Exp(meanLog) * gMean;
        double varMvue = Math.Exp(2.0 * meanLog)
            * (FinneysG(n - 1, 2.0 * s2) - FinneysG(n - 1, s2 * (n - 2.0) / (n - 1.0)));
        double sdMvue = varMvue > 0 ? Math.Sqrt(varMvue) : double.NaN;
        return (mvueMean, sdMvue);
    }

    /// <summary>
    /// Standard error of the MVUE mean (TG eq. 2-16, Bradu-Mundlak 1970):
    /// se(mu1) = sqrt(exp(2*meanlog) * (g(n-1, s2/2)^2 - g(n-1, s2*(n-2)/(n-1)))).
    /// This is the dispersion entering the Chebyshev (MVUE) UCL (TG eq. 2-47) —
    /// NOT the MVUE of the distribution sd (eq. 2-15). Verified against the
    /// ProUCL 5.2 live sheet on silver-data.xls (95% Chebyshev (MVUE) = 19.12).
    /// </summary>
    public static double MvueSe(double meanLog, double sdLog, int n)
    {
        if (n < 2) throw new ArgumentOutOfRangeException(nameof(n));
        double s2 = sdLog * sdLog;
        double gMean = FinneysG(n - 1, s2 / 2.0);
        double varEst = Math.Exp(2.0 * meanLog)
            * (gMean * gMean - FinneysG(n - 1, s2 * (n - 2.0) / (n - 1.0)));
        return varEst > 0 ? Math.Sqrt(varEst) : double.NaN;
    }

    /// <summary>Chebyshev UCL on the MVUEs (TG eq. 2-47).</summary>
    public static double ChebyshevMvueUcl(double mvueMean, double mvueSe, double cc = 0.95)
    {
        double alpha = 1.0 - cc;
        return mvueMean + Math.Sqrt(1.0 / alpha - 1.0) * mvueSe;
    }

    /// <summary>
    /// Hall's skewness-adjusted bootstrap UCL (TG eq. 2-63/2-64). Uses the seeded
    /// RNG; deterministic for a fixed seed. N resamples of size n.
    /// </summary>
    public static double HallBootstrap(ReadOnlySpan<double> data, double cc = 0.95,
        int bootstrapRuns = 2000, ulong seed = Ucl.DefaultBootstrapSeed)
    {
        int n = data.Length;
        if (n < 3) throw new ArgumentException("Hall bootstrap requires at least 3 observations.", nameof(data));
        double alpha = 1.0 - cc;

        double mean = 0;
        foreach (var v in data) mean += v;
        mean /= n;
        double ss = 0;
        foreach (var v in data) { double d = v - mean; ss += d * d; }
        double sd = Math.Sqrt(ss / (n - 1));
        if (sd <= 0) throw new ArgumentException("Hall bootstrap requires a nonzero sd.", nameof(data));
        double k3 = ThirdMoment(data) / (sd * sd * sd);   // sample skewness, eq. 2-43

        var pool = data.ToArray();
        var rng = new SeededRng(seed);
        var qs = new double[bootstrapRuns];
        for (int b = 0; b < bootstrapRuns; b++)
        {
            var sample = new double[n];
            for (int i = 0; i < n; i++) sample[i] = pool[rng.NextIndex(n)];
            double bm = 0;
            foreach (var v in sample) bm += v;
            bm /= n;
            double bss = 0, bcube = 0;
            foreach (var v in sample) { double d = v - bm; bss += d * d; bcube += d * d * d; }
            double bsd = Math.Sqrt(bss / (n - 1));
            // bootstrap skewness: eq. 2-43 with the unbiased third moment
            double bk3 = n * bcube / ((n - 1.0) * (n - 2.0) * Math.Pow(bss / (n - 1.0), 1.5));
            double w = bsd > 0 ? (bm - mean) / bsd : 0.0;
            qs[b] = w + bk3 * w * w / 3.0 + bk3 * bk3 * w * w * w / 27.0 + bk3 / (6.0 * n);
        }
        Array.Sort(qs);

        // q_a = the alpha*N-th ordered Q; invert through the cubic (eq. 2-63)
        double qA = Ordered(qs, alpha);
        double wInv = 3.0 * (Math.Cbrt(1.0 + k3 * (qA - k3 / (6.0 * n))) - 1.0) / k3;
        return mean - wInv * sd;

        static double Ordered(double[] sorted, double p)
        {
            int idx = Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1);
            return sorted[idx];
        }

        static double ThirdMoment(ReadOnlySpan<double> values)
        {
            int cnt = values.Length;
            double m = 0;
            foreach (var v in values) m += v;
            m /= cnt;
            double c3 = 0;
            foreach (var v in values) { double d = v - m; c3 += d * d * d; }
            return cnt * c3 / ((cnt - 1.0) * (cnt - 2.0));
        }
    }
}
