namespace OpenUCL.Core;

/// <summary>
/// Background threshold value (BTV) estimates for a full (uncensored) data set:
/// upper percentiles, upper prediction limits (UPL1, UPLk, UPLk-mean), upper
/// tolerance limits (UTL) and nonparametric USL, following ProUCL Technical
/// Guide 5.2 chapter 3. Parametric USLs require the Singh (1993) d2max tables
/// and are not included yet.
/// </summary>
public sealed record BtvResult
{
    public required int N { get; init; }
    public required double ConfidenceCoefficient { get; init; }   // 1 - alpha
    public required double Coverage { get; init; }                // p
    public required int FutureK { get; init; }                    // k future comparisons

    // Percentiles (upper p*100%)
    public double PercentileNonparametric { get; init; }
    public double PercentileNormal { get; init; }
    public bool LognormalApplicable { get; init; }
    public double PercentileLognormal { get; init; }
    public bool GammaApplicable { get; init; }
    public double PercentileGamma { get; init; }

    // Prediction limits
    public double UplNormal1 { get; init; }
    public double UplNormalK { get; init; }
    public double UplNormalKMean { get; init; }
    public double UplLognormal1 { get; init; }
    public double UplGammaWh { get; init; }        // Wilson-Hilferty (cube-root scale)
    public double UplGammaHw { get; init; }        // Hawkins-Wixley (log scale)
    public double UplNonparametric { get; init; }
    public double UplChebyshev { get; init; }

    // Tolerance limits
    public double ToleranceFactorK { get; init; }  // exact K(n, cc, coverage)
    public double UtlNormal { get; init; }
    public double UtlLognormal { get; init; }
    public double UtlGammaWh { get; init; }
    public double UtlGammaHw { get; init; }
    public double UtlNonparametric { get; init; }
    public int UtlNonparametricOrder { get; init; }    // r (order of the statistic used)
    public double UtlAchievedCC { get; init; }         // exact CC achieved by the order statistic

    // Simultaneous limits
    public double UslNonparametric { get; init; }      // largest observation
}

public static class Btv
{
    /// <summary>
    /// Computes the BTV suite. All values are computed on the raw data; for skewed
    /// data the gamma WH/HW limits use the cube-root / log transforms (TG sections
    /// 3.4.3 and 3.5.3).
    /// </summary>
    public static BtvResult Compute(ReadOnlySpan<double> data,
        double cc = 0.95, double coverage = 0.95, int futureK = 5)
    {
        int n = data.Length;
        if (n < 3) throw new ArgumentException("BTV computation requires at least 3 observations.", nameof(data));
        if (cc is < 0.5 or >= 1.0) throw new ArgumentOutOfRangeException(nameof(cc));
        if (coverage is <= 0.0 or >= 1.0) throw new ArgumentOutOfRangeException(nameof(coverage));
        if (futureK < 1) throw new ArgumentOutOfRangeException(nameof(futureK));
        double alpha = 1.0 - cc;

        double mean = 0;
        foreach (var v in data) mean += v;
        mean /= n;
        double sumSq = 0;
        foreach (var v in data) { double d = v - mean; sumSq += d * d; }
        double sd = Math.Sqrt(sumSq / (n - 1));

        var sorted = data.ToArray();
        Array.Sort(sorted);
        double zP = NormalDistribution.Quantile(coverage);
        double t = StudentT.Quantile(cc, n - 1);
        double tBonf = StudentT.Quantile(1.0 - alpha / futureK, n - 1);

        // Percentiles (TG 3.3)
        double ppNonparam = PercentileProUcl(sorted, coverage);
        double ppNormal = mean + sd * zP;

        bool logOk = true;
        foreach (var v in data) if (v <= 0) { logOk = false; break; }
        double meanLog = 0, sdLog = double.NaN, ppLog = double.NaN;
        if (logOk)
        {
            var logs = data.ToArray().Select(v => Math.Log(v)).ToArray();
            foreach (var v in logs) meanLog += v;
            meanLog /= n;
            double ssq = 0;
            foreach (var v in logs) { double d = v - meanLog; ssq += d * d; }
            sdLog = Math.Sqrt(ssq / (n - 1));
            ppLog = Math.Exp(meanLog + sdLog * zP);
        }

        bool gammaOk = logOk;
        double kHat = double.NaN, thHat = double.NaN, ppGamma = double.NaN;
        if (gammaOk)
        {
            var fit = GammaDistribution.FitMLE(data);
            kHat = fit.KHat; thHat = fit.ThetaHat;
            ppGamma = fit.ThetaHat * GammaDistribution.Quantile(coverage, fit.KHat, 1.0);   // eq. 3-5
        }

        // Prediction limits (TG 3.5)
        double sqrt1p = Math.Sqrt(1.0 + 1.0 / n);
        double uplN1 = mean + t * sd * sqrt1p;                                            // eq. 3-13
        double uplNk = mean + tBonf * sd * sqrt1p;                                        // eq. 3-18
        double uplNkMean = mean + t * sd * Math.Sqrt(1.0 / futureK + 1.0 / n);
        double uplL1 = logOk ? Math.Exp(meanLog + t * sdLog * sqrt1p) : double.NaN;       // eq. 3-14

        // Gamma WH (cube-root scale) and HW (fourth-root scale) limits, TG 3.4.3/3.5.3.
        double cbrtMean = 0, cbrtSd = double.NaN, fourthMean = 0, fourthSd = double.NaN;
        if (gammaOk)
        {
            var cbrt = data.ToArray().Select(v => Math.Cbrt(v)).ToArray();
            foreach (var v in cbrt) cbrtMean += v;
            cbrtMean /= n;
            double ssq = 0;
            foreach (var v in cbrt) { double d = v - cbrtMean; ssq += d * d; }
            cbrtSd = Math.Sqrt(ssq / (n - 1));

            var fourth = data.ToArray().Select(v => Math.Pow(v, 0.25)).ToArray();
            foreach (var v in fourth) fourthMean += v;
            fourthMean /= n;
            ssq = 0;
            foreach (var v in fourth) { double d = v - fourthMean; ssq += d * d; }
            fourthSd = Math.Sqrt(ssq / (n - 1));
        }
        double uplWh = gammaOk ? Math.Max(0.0, Math.Pow(cbrtMean + t * cbrtSd * sqrt1p, 3.0)) : double.NaN;   // eq. 3-15
        double uplHw = gammaOk ? Math.Pow(fourthMean + t * fourthSd * sqrt1p, 4.0) : double.NaN;               // eq. 3-16
        double uplNonparam = PositionInterp(sorted, (n + 1.0) * (1.0 - alpha));                                // eq. 3-17
        double uplCheby = mean + Math.Sqrt(1.0 / alpha - 1.0) * sd * sqrt1p;

        // Tolerance limits (TG 3.4)
        double kFactor = NoncentralT.ToleranceFactor(n, cc, coverage);
        double utlNormal = mean + kFactor * sd;                                            // eq. 3-6
        double utlLog = logOk ? Math.Exp(meanLog + kFactor * sdLog) : double.NaN;          // eq. 3-7
        double utlWh = gammaOk ? Math.Max(0.0, Math.Pow(cbrtMean + kFactor * cbrtSd, 3.0)) : double.NaN;   // eq. 3-8
        double utlHw = gammaOk ? Math.Pow(fourthMean + kFactor * fourthSd, 4.0) : double.NaN;               // eq. 3-9

        // Nonparametric UTL (TG 3.4.4.1): r is chosen so that the binomial cumulative
        // P(Bin(n, coverage) <= r) is as close as possible to the target CC; the UTL
        // is x(r) and the achieved CC is the F-based value of eq. 3-11 (equivalent to
        // the binomial cumulative at r-1). For Lead n=24: r = 24, achieved CC 0.708.
        int r = 1;
        double best = Math.Abs(AchievedCC(n, 1, coverage) - cc);
        for (int r2 = 2; r2 <= n; r2++)
        {
            double d = Math.Abs(AchievedCC(n, r2, coverage) - cc);
            if (d < best) { best = d; r = r2; }
        }
        double utlNonparam = sorted[r - 1];
        double achieved = AchievedCC(n, r, coverage);

        return new BtvResult
        {
            N = n, ConfidenceCoefficient = cc, Coverage = coverage, FutureK = futureK,
            PercentileNonparametric = ppNonparam, PercentileNormal = ppNormal,
            LognormalApplicable = logOk, PercentileLognormal = ppLog,
            GammaApplicable = gammaOk, PercentileGamma = ppGamma,
            UplNormal1 = uplN1, UplNormalK = uplNk, UplNormalKMean = uplNkMean,
            UplLognormal1 = uplL1, UplGammaWh = uplWh, UplGammaHw = uplHw,
            UplNonparametric = uplNonparam, UplChebyshev = uplCheby,
            ToleranceFactorK = kFactor, UtlNormal = utlNormal, UtlLognormal = utlLog,
            UtlGammaWh = utlWh, UtlGammaHw = utlHw,
            UtlNonparametric = utlNonparam, UtlNonparametricOrder = r,
            UtlAchievedCC = achieved,
            UslNonparametric = sorted[n - 1],
        };

        static int CountNonPositive(double[] values) { int c = 0; foreach (var v in values) if (v <= 0) c++; return c; }
    }

    /// <summary>
    /// Nonparametric percentile with the p*n order-statistic convention and linear
    /// interpolation — the algorithm ProUCL 4.1+ adopted from Excel (TG eq. 3-1).
    /// </summary>
    public static double PercentileProUcl(double[] sorted, double p)
    {
        double h = p * sorted.Length;
        if (h < 1) return sorted[0];
        if (h > sorted.Length) return sorted[^1];
        int lo = (int)Math.Floor(h);          // 1-based floor
        double frac = h - lo;
        if (lo >= sorted.Length) return sorted[^1];
        if (frac == 0) return sorted[lo - 1];
        return sorted[lo - 1] + frac * (sorted[lo] - sorted[lo - 1]);
    }

    /// <summary>Linear interpolation at the (1-based) order position m; clamped to [1, n] (TG eq. 3-17).</summary>
    public static double PositionInterp(double[] sorted, double m)
    {
        m = Math.Clamp(m, 1.0, sorted.Length);
        int lo = (int)Math.Floor(m);
        double frac = m - lo;
        if (lo >= sorted.Length) return sorted[^1];
        if (frac == 0) return sorted[lo - 1];
        return sorted[lo - 1] + frac * (sorted[lo] - sorted[lo - 1]);
    }

    /// <summary>
    /// Exact confidence coefficient achieved by a UTL based on the order statistic x(r)
    /// (ascending, r = n is the maximum): CC = sum_{j=0}^{r-1} C(n,j) p^j (1-p)^(n-j)
    /// (binomial; TG 3.4.4). For r = n: CC = 1 - (1-p)^n — 0.708 for n=24, p=0.95.
    /// </summary>
    public static double AchievedCC(int n, int r, double coverage)
    {
        if (r <= 0) return 0.0;
        if (r > n) return 1.0;
        double q = 1.0 - coverage;
        double sum = 0, coef = Math.Pow(q, n);
        for (int j = 0; j <= r - 1; j++)
        {
            sum += coef;
            coef = coef * (n - j) / (j + 1.0) * (coverage / q);
        }
        return Math.Min(Math.Max(sum, 0.0), 1.0);
    }
}
