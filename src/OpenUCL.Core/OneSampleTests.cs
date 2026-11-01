namespace OpenUCL.Core;

/// <summary>One-sample test outcome at the requested significance level.</summary>
public sealed record OneSampleTestResult
{
    public required int N { get; init; }
    public required double ComparisonValue { get; init; }   // Cs
    public required string TestForm { get; init; }          // "Form 1 (HA: mean < Cs)" / "Form 2 (HA: mean > Cs)"
    public required double Statistic { get; init; }
    public required double PValueOneSided { get; init; }
    public required bool RejectNull { get; init; }
}

/// <summary>
/// One-sample hypothesis tests for the mean/median against a comparison value Cs
/// (ProUCL Technical Guide 5.2 sections 6.6.1-6.6.4): Student's t, sign test and
/// Wilcoxon signed-rank test (exact via subset dynamic programming, with tie
/// correction not needed in the exact variant).
/// </summary>
public static class OneSampleTests
{
    /// <summary>Student's t test (section 6.6.1). upperTail=true -> HA: mean > Cs.</summary>
    public static OneSampleTestResult TTest(ReadOnlySpan<double> data, double cs, bool upperTail, double alpha = 0.05)
    {
        int n = data.Length;
        if (n < 2) throw new ArgumentException("t test requires at least 2 observations.", nameof(data));
        double mean = 0;
        foreach (var v in data) mean += v;
        mean /= n;
        double ss = 0;
        foreach (var v in data) { double d = v - mean; ss += d * d; }
        double sd = Math.Sqrt(ss / (n - 1));
        double se = sd / Math.Sqrt(n);
        double t = se > 0 ? (mean - cs) / se : 0.0;
        double p = upperTail
            ? 1.0 - StudentT.Cdf(t, n - 1)
            : StudentT.Cdf(t, n - 1);
        double tCrit = StudentT.Quantile(1.0 - alpha, n - 1);
        bool reject = upperTail ? t > tCrit : t < -tCrit;
        return new OneSampleTestResult
        {
            N = n, ComparisonValue = cs,
            TestForm = upperTail ? "Form 2 (HA: mean > Cs)" : "Form 1 (HA: mean < Cs)",
            Statistic = t, PValueOneSided = Math.Clamp(p, 0.0, 1.0), RejectNull = reject,
        };
    }

    /// <summary>
    /// Exact sign test (section 6.6.3): counts of observations above/below Cs;
    /// zeros are discarded. Binomial p-value with p = 1/2.
    /// </summary>
    public static OneSampleTestResult SignTest(ReadOnlySpan<double> data, double cs, bool upperTail, double alpha = 0.05)
    {
        int n = data.Length;
        int above = 0, below = 0;
        foreach (var v in data)
        {
            if (v > cs) above++;
            else if (v < cs) below++;
        }
        int effective = above + below;
        if (effective == 0)
            throw new ArgumentException("Sign test: all observations equal Cs.", nameof(data));

        // p-value of the observed number of "successes" (above for upper tail).
        int successes = upperTail ? above : below;
        double p = BinomialAtLeast(successes, effective, 0.5);
        double critCount = BinomialQuantileUpper(1.0 - alpha, effective);
        bool reject = successes >= critCount;
        return new OneSampleTestResult
        {
            N = n, ComparisonValue = cs,
            TestForm = upperTail ? "Form 2 (HA: median > Cs)" : "Form 1 (HA: median < Cs)",
            Statistic = successes,
            PValueOneSided = Math.Clamp(p, 0.0, 1.0), RejectNull = reject,
        };
    }

    /// <summary>
    /// Exact Wilcoxon signed-rank test (section 6.6.4): ranks of |x - Cs| with zeros
    /// discarded; ties share the average rank; W+ = sum of ranks of positive
    /// differences; exact p-value by subset dynamic programming over the ranks.
    /// </summary>
    public static OneSampleTestResult WilcoxonSignedRank(ReadOnlySpan<double> data, double cs, bool upperTail, double alpha = 0.05)
    {
        var diffs = new List<(double Diff, double Abs)>();
        foreach (var v in data)
        {
            double d = v - cs;
            if (d != 0) diffs.Add((d, Math.Abs(d)));
        }
        int n = diffs.Count;
        if (n < 1) throw new ArgumentException("Wilcoxon test: all differences are zero.", nameof(data));

        var sortedAbs = diffs
            .Select((d, idx) => (d.Abs, idx))
            .OrderBy(t => t.Abs)
            .ToArray();
        var ranks = new double[n];
        int i = 0;
        while (i < n)
        {
            int j = i;
            while (j + 1 < n && sortedAbs[j + 1].Abs == sortedAbs[i].Abs) j++;
            double avg = (i + j) / 2.0 + 1.0;   // average rank (1-based) for ties
            for (int k = i; k <= j; k++) ranks[sortedAbs[k].idx] = avg;
            i = j + 1;
        }
        double wPlus = 0;
        int positives = 0;
        for (int k = 0; k < n; k++)
        {
            if (diffs[k].Diff > 0) { wPlus += ranks[k]; positives++; }
        }

        // Exact null distribution of W+ over all 2^n sign assignments of the ranks,
        // computed in half-rank units so tied (average) ranks stay integral
        // (subset-sum DP; n is small — zeros are already discarded).
        double totalWeight = Math.Pow(2.0, n);
        var dist = new Dictionary<long, double> { [0] = 1.0 };
        for (int k = 0; k < n; k++)
        {
            long rk = (long)Math.Round(ranks[k] * 2.0);
            var next = new Dictionary<long, double>(dist.Count * 2);
            foreach (var (w, cnt) in dist)
            {
                next[w] = next.GetValueOrDefault(w) + cnt;
                next[w + rk] = next.GetValueOrDefault(w + rk) + cnt;
            }
            dist = next;
        }
        long wKey = (long)Math.Round(wPlus * 2.0);
        double atLeast = 0, atMost = 0;
        foreach (var (w, cnt) in dist)
        {
            if (w >= wKey) atLeast += cnt;
            if (w <= wKey) atMost += cnt;
        }
        double p = Math.Clamp(upperTail ? atLeast / totalWeight : atMost / totalWeight, 0.0, 1.0);

        // exact critical W+ at alpha (upper tail): smallest w whose upper tail <= alpha
        double? critHalf = null;
        double cum = 0;
        foreach (var (w, cnt) in dist.OrderBy(kv => kv.Key))
        {
            cum += cnt / totalWeight;
            if (cum >= 1.0 - alpha) { critHalf = w; break; }
        }
        bool reject = upperTail
            ? critHalf is not null && wPlus * 2.0 >= critHalf
            : p <= alpha;

        return new OneSampleTestResult
        {
            N = n, ComparisonValue = cs,
            TestForm = upperTail ? "Form 2 (HA: median > Cs)" : "Form 1 (HA: median < Cs)",
            Statistic = wPlus,
            PValueOneSided = p, RejectNull = reject,
        };
    }

    private static double BinomialAtLeast(int successes, int trials, double prob)
    {
        double p = 0;
        double logC = SpecialFunctions.LogGamma(trials + 1)
                    - SpecialFunctions.LogGamma(successes + 1)
                    - SpecialFunctions.LogGamma(trials - successes + 1);
        for (int j = successes; j <= trials; j++)
        {
            double logTerm = logC + j * Math.Log(prob) + (trials - j) * Math.Log(1.0 - prob);
            p += Math.Exp(logTerm);
            logC += Math.Log(trials - j) - Math.Log(j + 1.0);
        }
        return Math.Min(Math.Max(p, 0.0), 1.0);
    }

    /// <summary>Smallest count c such that P(Bin(trials, 1/2) >= c) <= 1 - conf.</summary>
    private static int BinomialQuantileUpper(double conf, int trials)
    {
        for (int c = trials; c >= 0; c--)
        {
            if (BinomialAtLeast(c, trials, 0.5) <= 1.0 - conf + 1e-12)
                return c;
        }
        return 0;
    }
}
