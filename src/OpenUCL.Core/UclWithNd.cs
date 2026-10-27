namespace OpenUCL.Core;

/// <summary>
/// UCL/EPC methods for left-censored data sets (with nondetects), following
/// ProUCL Technical Guide 5.2 chapter 5: KM estimates plugged into the normal /
/// Chebyshev UCL equations, bootstrap over observation pairs with the KM mean as
/// statistic, and the full UCL suite applied to the LROS/GROS imputed data sets.
/// </summary>
public sealed record UclWithNdResult
{
    public required int N { get; init; }
    public required int Nondetects { get; init; }
    public required double ConfidenceCoefficient { get; init; }

    // Kaplan-Meier based (TG sections 4.4/4.6)
    public required double KmMean { get; init; }
    public required double KmSd { get; init; }
    public required double KmSe { get; init; }
    public double KmT { get; init; }              // KM mean + t(alpha, n-1) * KM SE
    public double KmZ { get; init; }              // KM mean + z(alpha) * KM SE
    public double KmChebyshev { get; init; }      // KM mean + sqrt(1/alpha - 1) * KM SE
    public double KmPercentileBootstrap { get; init; }
    public double KmBcaBootstrap { get; init; }

    // ROS-based (full UCL suite on the imputed data set)
    public double LrosT { get; init; }
    public double LrosPercentileBootstrap { get; init; }
    public double GrosT { get; init; }
    public double GrosGammaAdjusted { get; init; }   // gamma adjusted UCL on GROS data (n<=50)
    public double GrosGammaApproximate { get; init; }
}

public static class UclWithNd
{
    /// <summary>
    /// Computes the UCL suite for a left-censored data set at confidence coefficient cc.
    /// bootstrapRuns/seed control the resampling methods (deterministic per seed).
    /// Requires at least 3 observations, at least 2 detects, and not-all-ND data.
    /// </summary>
    public static UclWithNdResult Compute(IReadOnlyList<ValueObservation> observations,
        double cc = 0.95, int bootstrapRuns = Ucl.DefaultBootstrapRuns, ulong seed = Ucl.DefaultBootstrapSeed)
    {
        int n = observations.Count;
        if (n < 3) throw new ArgumentException("UCL computation requires at least 3 observations.", nameof(observations));
        if (cc is < 0.5 or >= 1.0) throw new ArgumentOutOfRangeException(nameof(cc));
        double alpha = 1.0 - cc;
        int nd = observations.Count(o => o.IsNondetect);
        if (nd == n) throw new ArgumentException("All observations are nondetects.", nameof(observations));
        if (n - nd < 2) throw new ArgumentException("ROS/KM UCLs require at least 2 detected observations.", nameof(observations));

        var km = Censored.KaplanMeier(observations);
        double z = NormalDistribution.Quantile(cc);
        double t = StudentT.Quantile(cc, n - 1);
        double chebFactor = Math.Sqrt(1.0 / alpha - 1.0);

        // KM bootstrap: resample (value, flag) pairs; statistic = KM mean of the sample.
        var pool = observations.ToArray();
        var rng = new SeededRng(seed);
        var kmMeans = new double[bootstrapRuns];
        for (int b = 0; b < bootstrapRuns; b++)
        {
            var sample = new ValueObservation[n];
            for (int i = 0; i < n; i++) sample[i] = pool[rng.NextIndex(n)];
            kmMeans[b] = Censored.KaplanMeier(sample).Mean;
        }
        Array.Sort(kmMeans);

        // BCA pieces for the KM mean: bias z0 and jackknife acceleration.
        int less = 0;
        foreach (var m in kmMeans) if (m < km.Mean) less++;
        double z0 = less == 0 ? -6.0 : less == bootstrapRuns ? 6.0
            : NormalDistribution.Quantile(less / (double)bootstrapRuns);
        double jackSum = 0, jackSq = 0;
        for (int i = 0; i < n; i++)
        {
            var reduced = new List<ValueObservation>(n - 1);
            for (int j = 0; j < n; j++) if (j != i) reduced.Add(pool[j]);
            double mj = reduced.All(o => o.IsNondetect) ? double.NaN : Censored.KaplanMeier(reduced).Mean;
            if (!double.IsNaN(mj)) { jackSum += mj; jackSq += mj * mj; }
        }
        double jackMean = jackSum / n;
        double acc3 = 0, acc2 = 0;
        for (int i = 0; i < n; i++)
        {
            var reduced = new List<ValueObservation>(n - 1);
            for (int j = 0; j < n; j++) if (j != i) reduced.Add(pool[j]);
            if (reduced.All(o => o.IsNondetect)) continue;
            double d = jackMean - Censored.KaplanMeier(reduced).Mean;
            acc3 += d * d * d;
            acc2 += d * d;
        }
        double aHat = acc2 > 0 ? acc3 / (6.0 * Math.Pow(acc2, 1.5)) : 0.0;
        double z1 = NormalDistribution.Quantile(1.0 - alpha);
        double alpha2 = NormalDistribution.Cdf(z0 + (z0 + z1) / (1.0 - aHat * (z0 + z1)));

        var result = new UclWithNdResult
        {
            N = n, Nondetects = nd, ConfidenceCoefficient = cc,
            KmMean = km.Mean, KmSd = km.Sd, KmSe = km.StandardErrorOfMean,
            KmT = km.Mean + t * km.StandardErrorOfMean,
            KmZ = km.Mean + z * km.StandardErrorOfMean,
            KmChebyshev = km.Mean + chebFactor * km.StandardErrorOfMean,
            KmPercentileBootstrap = Ordered(kmMeans, 1.0 - alpha),
            KmBcaBootstrap = Ordered(kmMeans, Math.Clamp(alpha2, 1.0 / bootstrapRuns, 1.0 - 1.0 / bootstrapRuns)),
        };

        // ROS-based UCLs: the full uncensored suite on the imputed data sets.
        try
        {
            var lros = Censored.RosLognormal(observations);
            var uL = Ucl.Compute(lros.FullData, cc, bootstrapRuns, seed);
            result = result with { LrosT = uL.StudentT, LrosPercentileBootstrap = uL.PercentileBootstrap };
        }
        catch (ArgumentException) { /* degenerate on the log scale: skip */ }

        try
        {
            var gros = Censored.RosGamma(observations);
            var uG = Ucl.Compute(gros.FullData, cc, bootstrapRuns, seed);
            result = result with
            {
                GrosT = uG.StudentT,
                GrosGammaApproximate = uG.GammaApproximate,
                GrosGammaAdjusted = uG.GammaAdjusted,
            };
        }
        catch (ArgumentException) { /* e.g. non-positive imputed values: skip */ }

        return result;
    }

    /// <summary>The ceil(p*N)-th ordered statistic (ProUCL convention).</summary>
    private static double Ordered(double[] sorted, double p)
    {
        int idx = Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1);
        return sorted[idx];
    }
}
