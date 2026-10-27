namespace OpenUCL.Core;

/// <summary>
/// Deterministic pseudo-random numbers (xoshiro256**, fixed seed) so that
/// bootstrap UCLs are reproducible and auditable. The seed is chosen by the
/// caller and printed on the output sheets.
/// </summary>
public sealed class SeededRng
{
    private ulong _s0, _s1, _s2, _s3;

    public SeededRng(ulong seed)
    {
        // SplitMix64 expansion of the seed into the 256-bit state.
        _s0 = SplitMix(ref seed);
        _s1 = SplitMix(ref seed);
        _s2 = SplitMix(ref seed);
        _s3 = SplitMix(ref seed);
    }

    private static ulong SplitMix(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        ulong z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public ulong NextUInt64()
    {
        ulong result = (_s1 * 5) << 7;
        result = ((result << 45) | (result >> 19)) * 5;
        ulong t = _s1 << 17;
        _s2 ^= _s0; _s3 ^= _s1; _s1 ^= _s2; _s0 ^= _s3; _s2 ^= t;
        _s3 = (_s3 << 45) | (_s3 >> 19);
        return result;
    }

    /// <summary>Uniform double in [0, 1).</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>Uniform integer index in [0, boundExclusive).</summary>
    public int NextIndex(int boundExclusive) => (int)(NextUInt64() % (ulong)boundExclusive);
}

/// <summary>All UCL/EPC point estimates computed for one full (uncensored) sample.</summary>
public sealed record UclResult
{
    public required int N { get; init; }
    public required double Mean { get; init; }
    public required double StandardDeviation { get; init; }
    public required double Skewness { get; init; }          // k3-hat = mu3-hat / s^3 (TG eq. 2-38/2-43)
    public required double ConfidenceCoefficient { get; init; }

    // Parametric (normal / lognormal)
    public double StudentT { get; init; }                    // TG eq. 2-32
    public double ModifiedT { get; init; }                   // TG eq. 2-37
    public double Jackknife { get; init; }                   // equals the t-UCL for the mean
    public double Clt { get; init; }                         // TG eq. 2-40
    public double AdjustedClt { get; init; }                 // TG eq. 2-42
    public double ChebyshevMeanSd { get; init; }             // TG eq. 2-46

    // Gamma (TG eq. 2-34/2-35, table 2-2 adjusted significance)
    public bool GammaApplicable { get; init; }
    public double KHat { get; init; }
    public double KStar { get; init; }
    public double ThetaHat { get; init; }
    public double ThetaStar { get; init; }
    public double ChiSquareApprox { get; init; }             // chi2_{nu*}(alpha)
    public double BetaAdjusted { get; init; }                // adjusted significance level (table 2-2)
    public double ChiSquareAdjusted { get; init; }           // chi2_{nu*}(beta)
    public double GammaApproximate { get; init; }
    public double GammaAdjusted { get; init; }

    // Bootstrap (seeded, reproducible; TG section 2.4.9)
    public required int BootstrapRuns { get; init; }
    public required ulong BootstrapSeed { get; init; }
    public double StandardBootstrap { get; init; }
    public double PercentileBootstrap { get; init; }
    public double BcaBootstrap { get; init; }
    public double BootstrapT { get; init; }
}

/// <summary>
/// UCL/EPC computation methods for full (uncensored) data sets, following
/// ProUCL Technical Guide 5.2 chapter 2. KM-based and lognormal H/MVUE variants
/// are handled elsewhere / planned.
/// </summary>
public static class Ucl
{
    public const ulong DefaultBootstrapSeed = 381;
    public const int DefaultBootstrapRuns = 2000;

    /// <summary>Unbiased third central moment estimate (TG eq. 2-38).</summary>
    public static double ThirdCentralMoment(ReadOnlySpan<double> data)
    {
        int n = data.Length;
        double mean = 0;
        foreach (var v in data) mean += v;
        mean /= n;
        double sum3 = 0;
        foreach (var v in data) { double d = v - mean; sum3 += d * d * d; }
        return n * sum3 / ((n - 1.0) * (n - 2.0));
    }

    /// <summary>
    /// Computes the UCL suite at confidence coefficient cc (0.5..1).
    /// bootstrapRuns/seed control the resampling methods (deterministic per seed).
    /// </summary>
    public static UclResult Compute(ReadOnlySpan<double> data, double cc = 0.95,
        int bootstrapRuns = DefaultBootstrapRuns, ulong seed = DefaultBootstrapSeed)
    {
        int n = data.Length;
        if (n < 3) throw new ArgumentException("UCL computation requires at least 3 observations.", nameof(data));
        if (cc is < 0.5 or >= 1.0) throw new ArgumentOutOfRangeException(nameof(cc), "confidence coefficient must be in [0.5, 1).");
        double alpha = 1.0 - cc;

        double mean = 0;
        foreach (var v in data) mean += v;
        mean /= n;
        double sumSq = 0;
        foreach (var v in data) { double d = v - mean; sumSq += d * d; }
        double sd = Math.Sqrt(sumSq / (n - 1));
        double se = sd / Math.Sqrt(n);
        double mu3 = ThirdCentralMoment(data);
        double skewness = mu3 / (sd * sd * sd);

        double z = NormalDistribution.Quantile(cc);
        double t = StudentT.Quantile(cc, n - 1);

        // --- normal-based (TG 2.4.1, 2.4.4-2.4.7) ---
        double studentT = mean + t * se;                                     // eq. 2-32
        double modifiedT = mean + mu3 / (6.0 * sd * sd * n) + t * se;        // eq. 2-37
        double clt = mean + z * se;                                          // eq. 2-40
        double adjClt = mean + (z + skewness * (1.0 + 2.0 * z * z) / (6.0 * Math.Sqrt(n))) * se; // eq. 2-42
        double cheby = mean + Math.Sqrt(1.0 / alpha - 1.0) * se;             // eq. 2-46

        // --- gamma (TG 2.4.2, eq. 2-34/2-35, table 2-2) ---
        bool gammaOk = true;
        foreach (var v in data) if (v <= 0) { gammaOk = false; break; }
        double kHat = double.NaN, kStar = double.NaN, thHat = double.NaN, thStar = double.NaN;
        double chiApprox = double.NaN, betaAdj = double.NaN, chiAdj = double.NaN;
        double gammaApprox = double.NaN, gammaAdj = double.NaN;
        if (gammaOk)
        {
            var fit = GammaDistribution.FitMLE(data);
            kHat = fit.KHat; kStar = fit.KStar; thHat = fit.ThetaHat; thStar = fit.ThetaStar;
            double nuStar = 2.0 * n * kStar;
            chiApprox = GammaDistribution.Quantile(alpha, nuStar / 2.0, 2.0);
            gammaApprox = 2.0 * n * kStar * mean / chiApprox;                // eq. 2-34
            if (n <= 50)
            {
                betaAdj = AdjustedSignificance(alpha, n);                    // table 2-2
                chiAdj = GammaDistribution.Quantile(betaAdj, nuStar / 2.0, 2.0);
                gammaAdj = 2.0 * n * kStar * mean / chiAdj;                  // eq. 2-35
            }
        }

        // --- bootstrap (TG 2.4.9) ---
        var boot = Bootstrap(data, alpha, bootstrapRuns, seed);
        double jackknife = studentT; // the jackknife UCL of a mean equals the t-UCL (TG table note)

        return new UclResult
        {
            N = n, Mean = mean, StandardDeviation = sd, Skewness = skewness,
            ConfidenceCoefficient = cc,
            StudentT = studentT, ModifiedT = modifiedT, Jackknife = jackknife,
            Clt = clt, AdjustedClt = adjClt, ChebyshevMeanSd = cheby,
            GammaApplicable = gammaOk,
            KHat = kHat, KStar = kStar, ThetaHat = thHat, ThetaStar = thStar,
            ChiSquareApprox = chiApprox, BetaAdjusted = betaAdj, ChiSquareAdjusted = chiAdj,
            GammaApproximate = gammaApprox, GammaAdjusted = gammaAdj,
            BootstrapRuns = bootstrapRuns, BootstrapSeed = seed,
            StandardBootstrap = boot.Standard, PercentileBootstrap = boot.Percentile,
            BcaBootstrap = boot.Bca, BootstrapT = boot.BootstrapT,
        };
    }

    /// <summary>Adjusted significance level beta for the adjusted gamma UCL (TG table 2-2), linear in n.</summary>
    public static double AdjustedSignificance(double alpha, int n)
    {
        if (n > 50) return alpha;   // approximate UCL beyond n = 50: no adjustment
        // table rows: n -> beta; guard against 1-cc rounding (e.g. 0.050000000000000044)
        (int n, double b)[] row = alpha switch
        {
            <= 0.0100001 => new[] { (5, 0.0000), (10, 0.0015), (20, 0.0046), (40, 0.0070), (int.MaxValue, 0.0100) },
            <= 0.0500001 => new[] { (5, 0.0086), (10, 0.0267), (20, 0.0380), (40, 0.0440), (int.MaxValue, 0.0500) },
            _ => new[] { (5, 0.0432), (10, 0.0724), (20, 0.0866), (40, 0.0934), (int.MaxValue, 0.1000) },
        };
        for (int i = 0; i < row.Length - 1; i++)
        {
            if (n <= row[i].n)
                return row[i].b;
            if (n <= row[i + 1].n)
            {
                double frac = (n - row[i].n) / (double)(row[i + 1].n - row[i].n);
                return row[i].b + frac * (row[i + 1].b - row[i].b);
            }
        }
        return row[^1].b;
    }

    private static (double Standard, double Percentile, double Bca, double BootstrapT) Bootstrap(
        ReadOnlySpan<double> data, double alpha, int runs, ulong seed)
    {
        int n = data.Length;
        var pool = data.ToArray();
        double meanAll = Mean(pool);
        var rng = new SeededRng(seed);
        var means = new double[runs];
        var tStats = new double[runs];

        for (int b = 0; b < runs; b++)
        {
            double sum = 0, sumSq = 0;
            for (int i = 0; i < n; i++)
            {
                double v = pool[rng.NextIndex(n)];
                sum += v;
                sumSq += v * v;
            }
            double m = sum / n;
            means[b] = m;
            double varB = (sumSq - n * m * m) / (n - 1);
            double sdB = varB > 0 ? Math.Sqrt(varB) : 0.0;
            tStats[b] = sdB > 0 ? Math.Sqrt(n) * (m - meanAll) / sdB : 0.0;
        }

        Array.Sort(means);
        Array.Sort(tStats);

        // standard bootstrap (eq. 2-54..2-56)
        double bootMean = 0;
        foreach (var m in means) bootMean += m;
        bootMean /= runs;
        double sb = 0;
        foreach (var m in means) sb += (m - bootMean) * (m - bootMean);
        sb = Math.Sqrt(sb / (runs - 1));
        double standard = meanAll + NormalDistribution.Quantile(1.0 - alpha) * sb;

        // percentile bootstrap (eq. 2-57): the ceil((1-alpha)N)-th ordered mean
        double percentile = means[OrderIndex(1.0 - alpha, runs)];

        // bootstrap-t (eq. 2-62): the alphaN-th ordered pivotal (lower quantile)
        double tLower = tStats[OrderIndex(alpha, runs)];
        double se = StandardDeviation(data) / Math.Sqrt(n);
        double bootstrapT = meanAll - tLower * se;

        // BCA (eq. 2-58..2-61)
        int less = 0;
        foreach (var m in means) if (m < meanAll) less++;
        double z0 = NormalDistribution.Quantile(less / (double)runs);
        double meanJackSum = 0, meanJackSqSum = 0;
        for (int i = 0; i < n; i++)
        {
            double sum = 0;
            for (int j = 0; j < n; j++) if (j != i) sum += data[j];
            double mj = sum / (n - 1.0);
            meanJackSum += mj;
            meanJackSqSum += mj * mj;
        }
        double meanJack = meanJackSum / n;
        double acc3 = 0, acc2 = 0;
        for (int i = 0; i < n; i++)
        {
            double sum = 0;
            for (int j = 0; j < n; j++) if (j != i) sum += data[j];
            double d = meanAll - sum / (n - 1.0);
            acc3 += d * d * d;
            acc2 += d * d;
        }
        double aHat = acc3 / (6.0 * Math.Pow(acc2, 1.5));   // eq. 2-61 (meanJack term cancels)
        double z1 = NormalDistribution.Quantile(1.0 - alpha);
        double alpha2 = NormalDistribution.Cdf(z0 + (z0 + z1) / (1.0 - aHat * (z0 + z1)));   // eq. 2-59
        double bca = means[OrderIndex(Math.Clamp(alpha2, 1.0 / runs, 1.0 - 1.0 / runs), runs)];

        return (standard, percentile, bca, bootstrapT);
    }

    /// <summary>0-based index of the ceil(p*N)-th ordered statistic (ProUCL convention: x(950) for N=1000, p=0.95).</summary>
    private static int OrderIndex(double p, int runs)
    {
        int idx = (int)Math.Ceiling(p * runs) - 1;
        return Math.Clamp(idx, 0, runs - 1);
    }

    private static double Mean(ReadOnlySpan<double> data)
    {
        double s = 0;
        foreach (var v in data) s += v;
        return s / data.Length;
    }

    private static double StandardDeviation(ReadOnlySpan<double> data)
    {
        double m = Mean(data), s = 0;
        foreach (var v in data) { double d = v - m; s += d * d; }
        return Math.Sqrt(s / (data.Length - 1));
    }
}
