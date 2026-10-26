namespace OpenUCL.Core;

/// <summary>Result of the Shapiro-Wilk test (Royston 1995, algorithm AS R94) on a sample.</summary>
public sealed record ShapiroWilkResult(int N, double W, double PValue)
{
    /// <summary>
    /// Critical value of W at the given significance level (test rejects normality when W &lt; critical).
    /// Computed by inverting the Royston p-value formula, the same way the p-value is obtained.
    /// </summary>
    public double CriticalValue(double alpha)
    {
        if (N == 3)
        {
            // p = (6/pi) (asin(sqrt(W)) - asin(sqrt(3/4)))  =>  W = sin^2(asin(sqrt(3/4)) + alpha*pi/6)
            double s = Math.Asin(Math.Sqrt(3.0 / 4.0)) + alpha * Math.PI / 6.0;
            double w = Math.Pow(Math.Sin(s), 2.0);
            return Math.Clamp(w, 0.0, 1.0);
        }
        double z = NormalDistribution.Quantile(1.0 - alpha);
        if (N <= 11)
        {
            double mu = GoodnessOfFit.Poly(GoodnessOfFit.Eval3, N);
            double sigma = Math.Exp(GoodnessOfFit.Poly(GoodnessOfFit.Eval4, N));
            double yStar = mu + sigma * z;
            double gamma = -2.273 + 0.459 * N;
            return Math.Clamp(1.0 - Math.Exp(gamma - Math.Exp(-yStar)), 0.0, 1.0);
        }
        double u = Math.Log(N);
        double mu12 = GoodnessOfFit.Poly(GoodnessOfFit.Eval5, u);
        double sigma12 = Math.Exp(GoodnessOfFit.Poly(GoodnessOfFit.Eval6, u));
        return Math.Clamp(1.0 - Math.Exp(mu12 + sigma12 * z), 0.0, 1.0);
    }
}

/// <summary>Result of the Lilliefors-corrected Kolmogorov-Smirnov test for normality.</summary>
public sealed record LillieforsResult(int N, double D)
{
    /// <summary>
    /// Critical value at significance level alpha (test rejects when D &gt; critical).
    /// Uses the large-sample formula adopted by ProUCL 5.2 (Moling and Abdi 2007):
    /// CV = Factor / ((0.83 + n)/sqrt(n) - 0.01), with Factor = 1.035, 0.895, 0.819, 0.741
    /// for alpha = 0.01, 0.05, 0.10, 0.20.
    /// NOTE: for n &lt;= 50 ProUCL uses the exact Moling-Abdi table (Salkind 2007), which is
    /// not yet integrated here; for such samples this value is an approximation.
    /// </summary>
    public double CriticalValue(double alpha)
    {
        double factor = alpha switch
        {
            <= 0.01 => 1.035,
            <= 0.05 => 0.895,
            <= 0.10 => 0.819,
            _ => 0.741,
        };
        return factor / ((0.83 + N) / Math.Sqrt(N) - 0.01);
    }
}

/// <summary>Combined ProUCL-style conclusion for normality/lognormality of one sample.</summary>
public enum GofConclusion
{
    Normal,
    ApproximatelyNormal,   // the two tests disagree: one accepts, one rejects
    NotNormal,
}

public static class GoodnessOfFit
{
    /// <summary>
    /// Shapiro-Wilk test of normality following Royston (1995), algorithm AS R94,
    /// valid for n from 3 to 5000 (ProUCL applies it for n &lt;= 2000).
    /// </summary>
    public static ShapiroWilkResult ShapiroWilk(ReadOnlySpan<double> data)
    {
        int n = data.Length;
        if (n < 3) throw new ArgumentException("Shapiro-Wilk requires at least 3 observations.", nameof(data));
        if (n > 5000) throw new ArgumentException("Shapiro-Wilk (AS R94) supports at most 5000 observations.", nameof(data));

        double[] x = data.ToArray();
        Array.Sort(x);
        double mean = 0;
        foreach (double v in x) mean += v;
        mean /= n;

        double ss = 0;
        foreach (double v in x) { double d = v - mean; ss += d * d; }

        if (n == 3)
        {
            double b = Math.Sqrt(0.5) * (x[2] - x[0]);
            double w3 = ClampW(b * b / ss);
            return new ShapiroWilkResult(n, w3, PValue(w3, n));
        }

        // Blom normal scores and symmetric weights (AS R94).
        int n2 = n / 2;
        double m = n + 0.25;
        double[] a = new double[n2];
        double summ2 = 0;
        for (int i = 0; i < n2; i++)
        {
            a[i] = NormalDistribution.Quantile((i + 1 - 0.375) / m);
            summ2 += 2.0 * a[i] * a[i];
        }
        double ssumm2 = Math.Sqrt(summ2);
        double rsn = 1.0 / Math.Sqrt(n);

        a[0] = Poly(Eval1, rsn) - a[0] / ssumm2;

        if (n > 5)
        {
            a[1] = Poly(Eval2, rsn) - a[1] / ssumm2;
            summ2 -= 2.0 * a[0] * a[0] + 2.0 * a[1] * a[1];
            double denom = 1.0 - 2.0 * a[0] * a[0] - 2.0 * a[1] * a[1];
            double fac = Math.Sqrt(summ2 / denom);
            for (int i = 2; i < n2; i++) a[i] = -a[i] / fac;
        }
        else
        {
            summ2 -= 2.0 * a[0] * a[0];
            double fac = Math.Sqrt(summ2 / (1.0 - 2.0 * a[0] * a[0]));
            for (int i = 1; i < n2; i++) a[i] = -a[i] / fac;
        }

        // Numerator: sum of a_i (x_(n+1-i) - x_(i)) over the lower half (a_i > 0).
        // W is the squared correlation between the signed weight vector and the centered
        // data (Royston/R formulation): the weights are normalized by their squared norm,
        // which differs appreciably from 1 at small n.
        double ssa = 0;
        for (int i = 0; i < n2; i++) ssa += 2.0 * a[i] * a[i];
        double cross = 0;
        for (int i = 0; i < n2; i++) cross += a[i] * (x[n - 1 - i] - x[i]);
        double w = ClampW(cross * cross / (ssa * ss));
        return new ShapiroWilkResult(n, w, PValue(w, n));
    }

    private static double ClampW(double w) => Math.Clamp(w, 0.0, 1.0);

    // Royston p-value polynomial coefficient arrays (constant term first).
    internal static readonly double[] Eval1 = { 0, 0.221157, -0.147981, -2.07119, 4.434685, -2.706056 };
    internal static readonly double[] Eval2 = { 0, 0.042981, -0.293762, -1.752461, 5.682633, -3.582633 };
    internal static readonly double[] Eval3 = { 0.544, -0.39978, 0.025054, -0.0006714 };
    internal static readonly double[] Eval4 = { 1.3822, -0.77857, 0.062767, -0.0020322 };
    internal static readonly double[] Eval5 = { -1.5861, -0.31082, -0.083751, 0.0038915 };
    internal static readonly double[] Eval6 = { -0.4803, -0.082676, 0.0030302 };

    internal static double Poly(double[] coefficients, double x)
    {
        double result = 0;
        for (int i = coefficients.Length - 1; i >= 0; i--) result = result * x + coefficients[i];
        return result;
    }

    private static double PValue(double w, int n)
    {
        if (n == 3)
        {
            // Exact small-case formula (Royston 1995).
            double p = (6.0 / Math.PI) * (Math.Asin(Math.Sqrt(w)) - Math.Asin(Math.Sqrt(0.75)));
            return Math.Max(p, 0.0);
        }
        double w1 = Math.Max(1.0 - w, double.Epsilon);
        if (n <= 11)
        {
            double gamma = -2.273 + 0.459 * n;
            double y = Math.Log(w1);
            if (y >= gamma) return 1e-99;
            y = -Math.Log(gamma - y);
            double mu = Poly(Eval3, n);
            double sigma = Math.Exp(Poly(Eval4, n));
            return Math.Clamp(1.0 - NormalDistribution.Cdf((y - mu) / sigma), 0.0, 1.0);
        }
        double u = Math.Log(n);
        double mu12 = Poly(Eval5, u);
        double sigma12 = Math.Exp(Poly(Eval6, u));
        double y12 = Math.Log(w1);
        return Math.Clamp(1.0 - NormalDistribution.Cdf((y12 - mu12) / sigma12), 0.0, 1.0);
    }

    /// <summary>
    /// Lilliefors-corrected Kolmogorov-Smirnov statistic for normality with estimated
    /// mean and standard deviation (sample sd, n-1 denominator).
    /// </summary>
    public static LillieforsResult Lilliefors(ReadOnlySpan<double> data)
    {
        int n = data.Length;
        if (n < 3) throw new ArgumentException("Lilliefors test requires at least 3 observations.", nameof(data));

        double[] x = data.ToArray();
        Array.Sort(x);
        double mean = 0;
        foreach (double v in x) mean += v;
        mean /= n;
        double ss = 0;
        foreach (double v in x) { double d = v - mean; ss += d * d; }
        double sd = Math.Sqrt(ss / (n - 1));
        if (sd == 0) throw new ArgumentException("Lilliefors test requires a nonzero standard deviation.", nameof(data));

        double dPlus = 0, dMinus = 0;
        for (int i = 0; i < n; i++)
        {
            double z = (x[i] - mean) / sd;
            double phi = NormalDistribution.Cdf(z);
            dPlus = Math.Max(dPlus, (i + 1.0) / n - phi);
            dMinus = Math.Max(dMinus, phi - i / (double)n);
        }
        return new LillieforsResult(n, Math.Max(dPlus, dMinus));
    }

    /// <summary>
    /// ProUCL-style combination: the data set is concluded (approximately) normal when at
    /// least one of the two tests accepts normality at the given alpha (ProUCL Technical
    /// Guide 5.2, section 2.2.1).
    /// </summary>
    public static GofConclusion Conclude(ShapiroWilkResult sw, LillieforsResult lf, double alpha)
    {
        bool swPass = sw.W >= sw.CriticalValue(alpha);
        bool lfPass = lf.D <= lf.CriticalValue(alpha);
        return (swPass, lfPass) switch
        {
            (true, true) => GofConclusion.Normal,
            (false, false) => GofConclusion.NotNormal,
            _ => GofConclusion.ApproximatelyNormal,
        };
    }
}
