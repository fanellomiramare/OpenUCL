namespace OpenUCL.Core;

/// <summary>Two-sample test outcome at the requested significance level.</summary>
public sealed record TwoSampleTestResult
{
    public required int N1 { get; init; }
    public required int N2 { get; init; }
    public required double Mean1 { get; init; }
    public required double Mean2 { get; init; }
    public required string TestForm { get; init; }
    public required double Statistic { get; init; }
    public required double DegreesOfFreedom { get; init; }
    public required double PValueOneSided { get; init; }
    public required bool RejectNull { get; init; }
}

/// <summary>
/// Two-sample hypothesis tests for the means and dispersions of independent
/// samples (ProUCL Technical Guide 5.2 sections 6.7-6.9.1): Student's t with
/// pooled variance, Satterthwaite with unequal variances, Wilcoxon-Mann-Whitney
/// (normal approximation with tie and continuity corrections) and the F test
/// for the equality of two variances. "Sample 1" plays the role of the site
/// data set; the alternative forms follow TG 6.3 (Form 1: HA mu1 &lt; mu2;
/// Form 2: HA mu1 &gt; mu2).
/// </summary>
public static class TwoSampleTests
{
    private static (double Mean, double Sd) SampleStats(double[] values)
    {
        double mean = 0;
        foreach (var v in values) mean += v;
        mean /= values.Length;
        double ss = 0;
        foreach (var v in values) { double d = v - mean; ss += d * d; }
        return (mean, Math.Sqrt(ss / (values.Length - 1)));
    }

    /// <summary>Student's two-sample t test with pooled variance (TG 6.7.1).</summary>
    public static TwoSampleTestResult TTestEqualVariance(double[] sample1, double[] sample2,
        bool upperTail, double alpha = 0.05)
    {
        if (sample1.Length < 2 || sample2.Length < 2)
            throw new ArgumentException("t test requires at least 2 observations per sample.");
        var (m1, s1) = SampleStats(sample1);
        var (m2, s2) = SampleStats(sample2);
        int n1 = sample1.Length, n2 = sample2.Length;
        double sp2 = ((n1 - 1) * s1 * s1 + (n2 - 1) * s2 * s2) / (n1 + n2 - 2);
        double sp = Math.Sqrt(sp2);
        double se = sp * Math.Sqrt(1.0 / n1 + 1.0 / n2);
        double t = se > 0 ? (m1 - m2) / se : 0.0;
        double df = n1 + n2 - 2;
        double p = upperTail ? 1.0 - StudentT.Cdf(t, df) : StudentT.Cdf(t, df);
        double tCrit = StudentT.Quantile(1.0 - alpha, df);
        bool reject = upperTail ? t > tCrit : t < -tCrit;
        return new TwoSampleTestResult
        {
            N1 = n1, N2 = n2, Mean1 = m1, Mean2 = m2,
            TestForm = upperTail ? "Form 2 (HA: mu1 > mu2)" : "Form 1 (HA: mu1 < mu2)",
            Statistic = t, DegreesOfFreedom = df,
            PValueOneSided = Math.Clamp(p, 0.0, 1.0), RejectNull = reject,
        };
    }

    /// <summary>Satterthwaite two-sample t test with unequal variances (TG 6.7.2).</summary>
    public static TwoSampleTestResult TTestSatterthwaite(double[] sample1, double[] sample2,
        bool upperTail, double alpha = 0.05)
    {
        if (sample1.Length < 2 || sample2.Length < 2)
            throw new ArgumentException("t test requires at least 2 observations per sample.");
        var (m1, s1) = SampleStats(sample1);
        var (m2, s2) = SampleStats(sample2);
        int n1 = sample1.Length, n2 = sample2.Length;
        double v1 = s1 * s1 / n1, v2 = s2 * s2 / n2;
        double se = Math.Sqrt(v1 + v2);
        double t = se > 0 ? (m1 - m2) / se : 0.0;
        double df = (v1 + v2) * (v1 + v2) / ((v1 * v1) / (n1 - 1) + (v2 * v2) / (n2 - 1));
        double p = upperTail ? 1.0 - StudentT.Cdf(t, df) : StudentT.Cdf(t, df);
        double tCrit = StudentT.Quantile(1.0 - alpha, df);
        bool reject = upperTail ? t > tCrit : t < -tCrit;
        return new TwoSampleTestResult
        {
            N1 = n1, N2 = n2, Mean1 = m1, Mean2 = m2,
            TestForm = upperTail ? "Form 2 (HA: mu1 > mu2)" : "Form 1 (HA: mu1 < mu2)",
            Statistic = t, DegreesOfFreedom = df,
            PValueOneSided = Math.Clamp(p, 0.0, 1.0), RejectNull = reject,
        };
    }

    /// <summary>
    /// F test for the equality of two variances (TG 6.8.1): larger sample variance
    /// in the numerator, two-sided p-value.
    /// </summary>
    public static (double F, double PValueTwoSided, bool RejectNull, double Df1, double Df2) FTest(
        double[] sample1, double[] sample2, double alpha = 0.05)
    {
        if (sample1.Length < 2 || sample2.Length < 2)
            throw new ArgumentException("F test requires at least 2 observations per sample.");
        var (_, s1) = SampleStats(sample1);
        var (_, s2) = SampleStats(sample2);
        if (s1 == 0 || s2 == 0)
            throw new ArgumentException("F test requires nonzero sample variances.");
        double v1 = s1 * s1, v2 = s2 * s2;
        double f = Math.Max(v1, v2) / Math.Min(v1, v2);
        int dfLarge = (v1 >= v2 ? sample1.Length : sample2.Length) - 1;
        int dfSmall = (v1 >= v2 ? sample2.Length : sample1.Length) - 1;
        double x = dfLarge * f / (dfLarge * f + dfSmall);
        double pUpper = 1.0 - SpecialFunctions.BetaI(x, dfLarge / 2.0, dfSmall / 2.0);
        double pTwoSided = Math.Min(2.0 * pUpper, 1.0);
        double fCrit = FCriticalOneSided(1.0 - alpha / 2.0, dfLarge, dfSmall);
        return (f, pTwoSided, pUpper > 1.0 - alpha / 2.0 && f >= fCrit, dfLarge, dfSmall);
    }

    /// <summary>F distribution quantile by bisection on the BetaI-based CDF.</summary>
    public static double FCriticalOneSided(double p, int df1, int df2)
    {
        double lo = 0.0, hi = 1.0;
        double Cdf(double f) => SpecialFunctions.BetaI(df1 * f / (df1 * f + df2), df1 / 2.0, df2 / 2.0);
        while (Cdf(hi) < p) hi *= 2.0;
        for (int i = 0; i < 200; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (Cdf(mid) < p) lo = mid;
            else hi = mid;
        }
        return 0.5 * (lo + hi);
    }

    /// <summary>
    /// Wilcoxon-Mann-Whitney test (TG 6.9.1): U statistic with the normal
    /// approximation, continuity correction and tie correction. upperTail=true
    /// rejects when sample 1 tends to larger values.
    /// </summary>
    public static TwoSampleTestResult Wmw(double[] sample1, double[] sample2,
        bool upperTail, double alpha = 0.05)
    {
        int n1 = sample1.Length, n2 = sample2.Length, total = n1 + n2;
        if (n1 < 1 || n2 < 1) throw new ArgumentException("WMW requires non-empty samples.");

        // pooled ranks with average ranks for ties
        var pooled = sample1.Select(v => (v, g: 1))
            .Concat(sample2.Select(v => (v, g: 2)))
            .OrderBy(t => t.v)
            .ToArray();
        var rankOf = new double[total];
        int i = 0;
        while (i < total)
        {
            int j = i;
            while (j + 1 < total && pooled[j + 1].v == pooled[i].v) j++;
            double avg = (i + j) / 2.0 + 1.0;
            for (int k = i; k <= j; k++) rankOf[k] = avg;
            i = j + 1;
        }

        double rankSum1 = 0;
        for (int k = 0; k < total; k++) if (pooled[k].g == 1) rankSum1 += rankOf[k];
        double u1 = rankSum1 - n1 * (n1 + 1.0) / 2.0;

        // tie correction
        double tieTerm = 0;
        int k2 = 0;
        while (k2 < total)
        {
            int j = k2;
            while (j + 1 < total && pooled[j + 1].v == pooled[k2].v) j++;
            int t = j - k2 + 1;
            if (t > 1) tieTerm += (double)t * t * t - t;
            k2 = j + 1;
        }
        double variance = n1 * n2 / 12.0 * ((total + 1.0) - tieTerm / (total * (total - 1.0)));
        double sd = Math.Sqrt(Math.Max(variance, 0.0));

        // continuity correction by 0.5 toward the null in the direction of the test
        double z = sd > 0 ? (upperTail ? u1 - 0.5 : u1 + 0.5) - n1 * n2 / 2.0 : 0.0;
        z /= sd;
        double p = upperTail ? 1.0 - NormalDistribution.Cdf(z) : NormalDistribution.Cdf(z);
        p = Math.Clamp(p, 0.0, 1.0);
        double zCrit = NormalDistribution.Quantile(1.0 - alpha);
        bool reject = upperTail ? z > zCrit : z < -zCrit;
        return new TwoSampleTestResult
        {
            N1 = n1, N2 = n2,
            Mean1 = sample1.Average(), Mean2 = sample2.Average(),
            TestForm = upperTail ? "Form 2 (HA: sample1 > sample2)" : "Form 1 (HA: sample1 < sample2)",
            Statistic = z, DegreesOfFreedom = double.NaN,
            PValueOneSided = p, RejectNull = reject,
        };
    }
}
