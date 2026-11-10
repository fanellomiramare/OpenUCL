namespace OpenUCL.Core;

/// <summary>Gehan or Tarone-Ware two-sample test result for left-censored data.</summary>
public sealed record CensoredTwoSampleResult
{
    public required int NSite { get; init; }
    public required int NBackground { get; init; }
    public required string TestName { get; init; }
    public required string TestForm { get; init; }
    public required double Statistic { get; init; }
    public required double PValueOneSided { get; init; }   // form-specific (site vs background)
    public required bool RejectNull { get; init; }
}

/// <summary>
/// Nonparametric two-sample tests for left-censored data with multiple detection
/// limits (ProUCL Technical Guide 5.2 sections 6.9.2-6.9.3): Gehan test and
/// Tarone-Ware test, normal-approximation based, for site vs background samples
/// with m >= 10 and n >= 10. Nondetects enter with their detection limit value
/// and keep their ND flag (TG 6.9.2 STEP 2/4: flagged values are listed and
/// counted in the at-risk totals, but only real detections are "events").
/// h = 1 marks site observations; Form 1 = HA site > background.
/// </summary>
public static class CensoredTwoSample
{
    /// <summary>Gehan test (TG 6.9.2): midrank scores a(R) = 2R - N - 1.</summary>
    public static CensoredTwoSampleResult Gehan(IReadOnlyList<ValueObservation> site,
        IReadOnlyList<ValueObservation> background, bool upperTail, double alpha = 0.05)
        => Compute(site, background, upperTail, alpha, taroneWare: false);

    /// <summary>Tarone-Ware test (TG 6.9.3): sqrt(n_i) weights on at-risk counts.</summary>
    public static CensoredTwoSampleResult TaroneWare(IReadOnlyList<ValueObservation> site,
        IReadOnlyList<ValueObservation> background, bool upperTail, double alpha = 0.05)
        => Compute(site, background, upperTail, alpha, taroneWare: true);

    private static CensoredTwoSampleResult Compute(IReadOnlyList<ValueObservation> site,
        IReadOnlyList<ValueObservation> background, bool upperTail, double alpha, bool taroneWare)
    {
        int m = background.Count, n = site.Count;
        if (m < 10 || n < 10)
            throw new ArgumentException("Gehan/Tarone-Ware require at least 10 observations per sample (normal approximation).");
        int total = m + n;

        var pooled = site.Select(o => (o.Value, IsDetect: !o.IsNondetect, h: 1))
            .Concat(background.Select(o => (o.Value, IsDetect: !o.IsNondetect, h: 0)))
            .OrderBy(t => t.Value)
            .ToArray();

        // ---- Gehan: midrank scores a(R) = 2R - N - 1 over the pooled sample ----
        double sumA = 0, sumA2 = 0;
        double siteScoreSum = 0;
        int i = 0;
        while (i < total)
        {
            int j = i;
            while (j + 1 < total && pooled[j + 1].Value == pooled[i].Value) j++;
            double avg = (i + j) / 2.0 + 1.0;
            double score = 2.0 * avg - total - 1.0;
            for (int k = i; k <= j; k++)
            {
                sumA += score;
                sumA2 += score * score;
                if (pooled[k].h == 1) siteScoreSum += score;
            }
            i = j + 1;
        }

        if (!taroneWare)
        {
            double varianceTerm = total / (double)(m * n) * sumA2;
            double g = varianceTerm > 0 ? siteScoreSum / Math.Sqrt(varianceTerm) : 0.0;
            return BuildResult(n, m, "Gehan", g, upperTail, alpha);
        }

        // ---- Tarone-Ware (TG 6.9.3.2, steps 2-7) ----
        // At each distinct value z (ascending), with the TG "at risk" convention:
        //   n_i = #obs <= z          (combined at risk)
        //   A_i = #combined DETECTED observations equal to z (an "event")
        //   d_i = #site DETECTED observations equal to z
        //   m_i = #site obs <= z     (site at risk)
        //   l_i = #background obs <= z (background at risk)
        //   E_i = A_i * m_i / n_i ;  V_i = A_i (n_i - A_i) m_i l_i / (n_i^2 (n_i - 1))
        //   TW = sum w_i (d_i - E_i) / sqrt(sum w_i^2 V_i),  w_i = sqrt(n_i)
        double num = 0, den = 0;
        int pos = 0;
        while (pos < total)
        {
            double z = pooled[pos].Value;
            int j = pos;
            while (j + 1 < total && pooled[j + 1].Value == z) j++;

            int atRisk = 0, aEvents = 0, dSite = 0, mSite = 0, lBg = 0;
            for (int k = 0; k < total; k++)
            {
                if (pooled[k].Value > z) continue;
                atRisk++;
                if (pooled[k].h == 1)
                {
                    mSite++;
                    if (pooled[k].Value == z && !pooled[k].IsDetect) continue; // site ND at z: no event
                    if (pooled[k].Value == z) { aEvents++; dSite++; }
                }
                else
                {
                    lBg++;
                    if (pooled[k].Value == z && pooled[k].IsDetect) aEvents++; // background detect at z
                }
            }

            if (aEvents > 0 && atRisk > 1)
            {
                double e = aEvents * mSite / (double)atRisk;
                double v = (double)aEvents * (atRisk - aEvents) * mSite * lBg
                    / (atRisk * (double)atRisk * (atRisk - 1));
                double w = Math.Sqrt(atRisk);
                num += w * (dSite - e);
                den += w * w * v;
            }
            pos = j + 1;
        }

        double tw = den > 0 ? num / Math.Sqrt(den) : 0.0;
        return BuildResult(n, m, "Tarone-Ware", tw, upperTail, alpha);
    }

    private static CensoredTwoSampleResult BuildResult(int n, int m, string name,
        double statistic, bool upperTail, double alpha)
    {
        double p = upperTail
            ? 1.0 - NormalDistribution.Cdf(statistic)
            : NormalDistribution.Cdf(statistic);
        p = Math.Clamp(p, 0.0, 1.0);
        double zCrit = NormalDistribution.Quantile(1.0 - alpha);
        bool reject = upperTail ? statistic > zCrit : statistic < -zCrit;
        return new CensoredTwoSampleResult
        {
            NSite = n, NBackground = m, TestName = name,
            TestForm = upperTail ? "Form 1 (HA: site > background)" : "Form 2 (HA: site < background)",
            Statistic = statistic, PValueOneSided = p, RejectNull = reject,
        };
    }
}
