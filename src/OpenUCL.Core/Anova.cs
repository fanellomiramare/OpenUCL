namespace OpenUCL.Core;

/// <summary>One-way ANOVA result (classical, TG 5.2 chapter 9) and Kruskal-Wallis.</summary>
public sealed record AnovaResult
{
    public required int Groups { get; init; }
    public required int N { get; init; }
    public required double SSBetween { get; init; }
    public required double SSWithin { get; init; }
    public required double DfBetween { get; init; }
    public required double DfWithin { get; init; }
    public required double F { get; init; }
    public required double PValue { get; init; }          // upper tail
    public bool RejectNull => PValue <= 1 - CC;
    public required double CC { get; init; }

    // Kruskal-Wallis
    public required double KwH { get; init; }
    public required double KwPValue { get; init; }         // upper tail chi-square
    public required double KwPValueTieCorrected { get; init; }
    public required bool KwRejectNull { get; init; }
}

/// <summary>
/// One-way ANOVA (classical) and Kruskal-Wallis nonparametric omnibus test across
/// k groups (ProUCL Technical Guide 5.2 chapter 9). Omnibus tests only: no
/// pairwise post-hoc comparisons.
/// </summary>
public static class Anova
{
    /// <summary>
    /// Computes the classical one-way ANOVA F test and the Kruskal-Wallis H test
    /// (with tie correction) across the groups at confidence coefficient cc.
    /// </summary>
    public static AnovaResult Compute(double[][] groups, double cc = 0.95)
    {
        if (groups.Length < 2)
            throw new ArgumentException("ANOVA requires at least 2 groups.", nameof(groups));
        foreach (var g in groups)
            if (g.Length < 1)
                throw new ArgumentException("Every group needs at least 1 observation.", nameof(groups));

        int k = groups.Length;
        int total = groups.Sum(g => g.Length);
        double grandMean = 0;
        foreach (var g in groups) foreach (var v in g) grandMean += v;
        grandMean /= total;

        // Classical one-way ANOVA.
        double ssBetween = 0, ssWithin = 0;
        foreach (var g in groups)
        {
            double gm = 0;
            foreach (var v in g) gm += v;
            gm /= g.Length;
            ssBetween += g.Length * (gm - grandMean) * (gm - grandMean);
            foreach (var v in g) { double d = v - gm; ssWithin += d * d; }
        }
        double dfB = k - 1, dfW = total - k;
        double msB = ssBetween / dfB, msW = dfW > 0 ? ssWithin / dfW : double.NaN;
        double f = msW > 0 ? msB / msW : double.NaN;
        double p = double.IsNaN(f) ? 1.0 : 1.0 - FCdf(f, dfB, dfW);

        // Kruskal-Wallis H with average ranks and tie correction.
        var pooled = groups.SelectMany((g, gi) => g.Select(v => (v, gi)))
            .OrderBy(t => t.v).ToArray();
        var rankSums = new double[k];
        int i = 0;
        double tieSum = 0;
        while (i < total)
        {
            int j = i;
            while (j + 1 < total && pooled[j + 1].v == pooled[i].v) j++;
            double avg = (i + j) / 2.0 + 1.0;
            int t = j - i + 1;
            if (t > 1) tieSum += (double)t * t * t - t;
            for (int kk = i; kk <= j; kk++) rankSums[pooled[kk].gi] += avg;
            i = j + 1;
        }
        double h = 0;
        for (int gi = 0; gi < k; gi++)
            h += rankSums[gi] * rankSums[gi] / groups[gi].Length;
        h = 12.0 / (total * (total + 1.0)) * h - 3.0 * (total + 1.0);
        double tieCorrection = 1.0;
        if (tieSum > 0 && total > 1)
            tieCorrection = 1.0 - tieSum / (Math.Pow(total, 3) - total);
        double hCorrected = h / tieCorrection;
        double kwP = 1.0 - ChiSquareCdf(hCorrected, k - 1);

        return new AnovaResult
        {
            Groups = k, N = total,
            SSBetween = ssBetween, SSWithin = ssWithin,
            DfBetween = dfB, DfWithin = dfW,
            F = f, PValue = Math.Clamp(p, 0.0, 1.0), CC = cc,
            KwH = hCorrected, KwPValue = Math.Clamp(kwP, 0.0, 1.0),
            KwPValueTieCorrected = Math.Clamp(kwP, 0.0, 1.0),
            KwRejectNull = 1.0 - kwP >= cc,
        };
    }

    /// <summary>Chi-square CDF: P(Y <= x), Y ~ chi-square with df degrees of freedom.</summary>
    public static double ChiSquareCdf(double x, double df) =>
        x <= 0 ? 0.0 : SpecialFunctions.GammaP(df / 2.0, x / 2.0);

    /// <summary>Chi-square upper-tail probability P(Y > x).</summary>
    public static double ChiSquareUpperTail(double x, double df) =>
        x <= 0 ? 1.0 : SpecialFunctions.GammaQ(df / 2.0, x / 2.0);

    /// <summary>Chi-square quantile: P(Y <= q) = p.</summary>
    public static double ChiSquareQuantile(double p, double df) =>
        df <= 0 ? throw new ArgumentOutOfRangeException(nameof(df))
        : GammaDistribution.Quantile(p, df / 2.0, 2.0);

    /// <summary>F distribution CDF: P(F <= f) with (df1, df2) degrees of freedom.</summary>
    public static double FCdf(double f, double df1, double df2) =>
        f <= 0 ? 0.0 : SpecialFunctions.BetaI(df1 * f / (df1 * f + df2), df1 / 2.0, df2 / 2.0);
}
