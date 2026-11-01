namespace OpenUCL.Core;

/// <summary>Trend analysis of a series ordered by time (TG 5.2 sections on trend: OLS, Mann-Kendall, Theil-Sen).</summary>
public sealed record TrendResult
{
    public required int N { get; init; }

    // Mann-Kendall
    public required double MkS { get; init; }             // S statistic (signed)
    public required double MkZ { get; init; }             // continuity-corrected z
    public required double MkPValueTwoSided { get; init; }
    public required string MkTrend { get; init; }         // "increasing" / "decreasing" / "no trend"

    // Theil-Sen
    public required double TheilSenSlope { get; init; }   // median of pairwise slopes

    // OLS regression on time index (0-based)
    public required double OlsSlope { get; init; }
    public required double OlsIntercept { get; init; }
    public required double OlsR2 { get; init; }
    public required double OlsSlopePValue { get; init; }  // two-sided
}

public static class Trend
{
    /// <summary>
    /// Mann-Kendall trend test, Theil-Sen slope and OLS regression against the
    /// observation order (time index 0..n-1). Requires at least 4 observations.
    /// </summary>
    public static TrendResult Compute(ReadOnlySpan<double> data)
    {
        int n = data.Length;
        if (n < 4) throw new ArgumentException("Trend analysis requires at least 4 observations.", nameof(data));
        double[] x = data.ToArray();

        // ---- Mann-Kendall ----
        double s = 0;
        for (int i = 0; i < n - 1; i++)
            for (int j = i + 1; j < n; j++)
                s += Math.Sign(x[j] - x[i]);

        // variance with tie correction (groups of equal values)
        var groups = new SortedDictionary<double, int>();
        foreach (var v in x) groups[v] = groups.GetValueOrDefault(v) + 1;
        double tieTerm = 0;
        foreach (var t in groups.Values)
            if (t > 1) tieTerm += t * (t - 1.0) * (2.0 * t + 5.0);
        double variance = (n * (n - 1.0) * (2.0 * n + 5.0) - tieTerm) / 18.0;

        double z = variance <= 0 ? 0.0
            : s > 0 ? (s - 1.0) / Math.Sqrt(variance)
            : s < 0 ? (s + 1.0) / Math.Sqrt(variance)
            : 0.0;
        double pTwo = Math.Clamp(2.0 * (1.0 - NormalDistribution.Cdf(Math.Abs(z))), 0.0, 1.0);
        string direction = pTwo <= 0.05 && s > 0 ? "increasing"
            : pTwo <= 0.05 && s < 0 ? "decreasing"
            : "no trend";

        // ---- Theil-Sen slope: median of the pairwise slopes ----
        var slopes = new List<double>();
        for (int i = 0; i < n - 1; i++)
            for (int j = i + 1; j < n; j++)
                if (x[j] != x[i]) slopes.Add((x[j] - x[i]) / (j - i));
        slopes.Sort();
        double sen = slopes.Count > 0 ? PercentileMedian(slopes) : 0.0;

        // ---- OLS on the time index ----
        double tMean = (n - 1.0) / 2.0;
        double yMean = 0;
        foreach (var v in x) yMean += v;
        yMean /= n;
        double sxx = 0, sxy = 0, syy = 0;
        for (int i = 0; i < n; i++)
        {
            double dt = i - tMean, dy = x[i] - yMean;
            sxx += dt * dt; sxy += dt * dy; syy += dy * dy;
        }
        double slope = sxx > 0 ? sxy / sxx : 0.0;
        double intercept = yMean - slope * tMean;
        double r2 = syy > 0 ? (sxy * sxy) / (sxx * syy) : 0.0;
        double seSlope = sxx > 0 ? Math.Sqrt(Math.Max((syy - slope * sxy) / (n - 2), 0) / sxx) : double.NaN;
        // perfect fit: zero residual variance gives an infinite t statistic
        double pSlope = seSlope > 0
            ? Math.Clamp(2.0 * (1.0 - StudentT.Cdf(Math.Abs(slope / seSlope), n - 2)), 0.0, 1.0)
            : slope != 0 ? 0.0 : 1.0;

        return new TrendResult
        {
            N = n,
            MkS = s, MkZ = z, MkPValueTwoSided = pTwo, MkTrend = direction,
            TheilSenSlope = sen,
            OlsSlope = slope, OlsIntercept = intercept,
            OlsR2 = r2, OlsSlopePValue = pSlope,
        };
    }

    private static double PercentileMedian(List<double> sorted)
    {
        int m = sorted.Count;
        return m % 2 == 1 ? sorted[m / 2] : 0.5 * (sorted[m / 2 - 1] + sorted[m / 2]);
    }
}
