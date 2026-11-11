namespace OpenUCL.Core;

/// <summary>
/// Background threshold values for left-censored data sets, computed with
/// Kaplan-Meier estimates (ProUCL Technical Guide 5.2 sections 5.3.1-5.3.3):
/// UPL1/UPLk and percentiles from the KM mean/sd, UTL with the exact tolerance
/// factor K, lognormal and gamma WH/HW variants with KM on the transformed
/// data (detects and NDs at their DL, all transformed to y-space).
/// </summary>
public sealed record BtvNdResult
{
    public required int N { get; init; }
    public required int Nondetects { get; init; }
    public required double ConfidenceCoefficient { get; init; }
    public required double Coverage { get; init; }
    public required int FutureK { get; init; }

    public required double KmMean { get; init; }
    public required double KmSd { get; init; }

    // UPLs (normal-KM, TG eq. 5-1 and chapter-3 k variants)
    public double UplNormal1 { get; init; }
    public double UplNormalK { get; init; }
    public double UplNormalKMean { get; init; }
    public double UplChebyshev { get; init; }                  // TG eq. 5-2
    public bool LognormalApplicable { get; init; }
    public double UplLognormal1 { get; init; }                 // TG 5.3.1.5
    public bool GammaApplicable { get; init; }
    public double UplGammaWh { get; init; }
    public double UplGammaHw { get; init; }

    // Percentiles (TG 5.3.2)
    public double PercentileNormal { get; init; }              // TG eq. 5-3
    public double PercentileLognormal { get; init; }
    public double PercentileGammaWh { get; init; }
    public double PercentileGammaHw { get; init; }

    // UTLs (TG 5.3.3)
    public double ToleranceFactorK { get; init; }
    public double UtlNormal { get; init; }                     // TG eq. 5-4
    public double UtlLognormal { get; init; }
    public double UtlGammaWh { get; init; }
    public double UtlGammaHw { get; init; }

    // Simultaneous limits (TG 5.3.4)
    public double D2Max { get; init; }
    public double UslNormal { get; init; }
    public double UslLognormal { get; init; }
    public double UslGammaWh { get; init; }
    public double UslGammaHw { get; init; }
    public double UslNonparametric { get; init; }      // largest observation (incl. NDs at DL)
}

public static class BtvWithNd
{
    /// <summary>Computes the KM-based BTV suite at the given confidence/coverage.</summary>
    public static BtvNdResult Compute(IReadOnlyList<ValueObservation> observations,
        double cc = 0.95, double coverage = 0.95, int futureK = 5)
    {
        int n = observations.Count;
        if (n < 3) throw new ArgumentException("BTV-ND requires at least 3 observations.", nameof(observations));
        if (cc is < 0.5 or >= 1.0) throw new ArgumentOutOfRangeException(nameof(cc));
        if (coverage is <= 0.0 or >= 1.0) throw new ArgumentOutOfRangeException(nameof(coverage));
        if (futureK < 1) throw new ArgumentOutOfRangeException(nameof(futureK));
        double alpha = 1.0 - cc;

        var km = Censored.KaplanMeier(observations);
        double t = StudentT.Quantile(cc, n - 1);
        double tBonf = StudentT.Quantile(1.0 - alpha / futureK, n - 1);
        double zP = NormalDistribution.Quantile(coverage);
        double sqrt1p = Math.Sqrt(1.0 + 1.0 / n);
        double kFactor = NoncentralT.ToleranceFactor(n, cc, coverage);

        // normal-based (eq. 5-1, 5-3, 5-4 and chapter-3 k variants)
        double uplN1 = km.Mean + t * km.Sd * sqrt1p;
        double uplNk = km.Mean + tBonf * km.Sd * sqrt1p;
        double uplNkMean = km.Mean + t * km.Sd * Math.Sqrt(1.0 / futureK + 1.0 / n);
        double uplCheby = km.Mean + Math.Sqrt(1.0 / alpha - 1.0) * km.Sd * sqrt1p;   // eq. 5-2
        double pctNormal = km.Mean + zP * km.Sd;
        double utlNormal = km.Mean + kFactor * km.Sd;                                 // eq. 5-4

        // transformed-scale variants: KM is recomputed on the transformed values
        // (detects and NDs at their DL, all transformed to y-space — TG 5.3.x).
        bool allPositive = observations.All(o => o.Value > 0);

        double uplLog = double.NaN, pctLog = double.NaN, utlLog = double.NaN, uslLog = double.NaN;
        KmEstimates? kmLog = null;
        if (allPositive)
        {
            kmLog = KmTransformed(observations, v => Math.Log(v));
            if (double.IsNaN(kmLog.Mean) || double.IsNaN(kmLog.Sd)) { /* skip */ }
            else
            {
                uplLog = Math.Exp(kmLog.Mean + t * kmLog.Sd * sqrt1p);
                pctLog = Math.Exp(kmLog.Mean + kmLog.Sd * zP);
                utlLog = Math.Exp(kmLog.Mean + kFactor * kmLog.Sd);
            }
        }

        double uplWh = double.NaN, uplHw = double.NaN, pctWh = double.NaN, pctHw = double.NaN;
        double utlWh = double.NaN, utlHw = double.NaN, uslWh = double.NaN, uslHw = double.NaN;
        KmEstimates? kmCbrt = null, kmRoot4 = null;
        if (allPositive)
        {
            kmCbrt = KmTransformed(observations, v => Math.Cbrt(v));
            kmRoot4 = KmTransformed(observations, v => Math.Pow(v, 0.25));
            if (!double.IsNaN(kmCbrt.Mean) && !double.IsNaN(kmCbrt.Sd))
            {
                uplWh = Math.Max(0.0, Math.Pow(kmCbrt.Mean + t * kmCbrt.Sd * sqrt1p, 3.0));
                pctWh = Math.Max(0.0, Math.Pow(kmCbrt.Mean + kmCbrt.Sd * zP, 3.0));
                utlWh = Math.Max(0.0, Math.Pow(kmCbrt.Mean + kFactor * kmCbrt.Sd, 3.0));
            }
            if (!double.IsNaN(kmRoot4.Mean) && !double.IsNaN(kmRoot4.Sd))
            {
                uplHw = Math.Pow(kmRoot4.Mean + t * kmRoot4.Sd * sqrt1p, 4.0);
                pctHw = Math.Pow(kmRoot4.Mean + kmRoot4.Sd * zP, 4.0);
                utlHw = Math.Pow(kmRoot4.Mean + kFactor * kmRoot4.Sd, 4.0);
            }
        }

        // Simultaneous limits (TG 5.3.4): the chapter-3 USL formulas applied to
        // the KM estimates, with the same Singh (1993) d2max critical value
        // computed on the total sample size n (detects and NDs at their DL).
        double d2max = Btv.D2Max(n, cc);
        double uslNormal = km.Mean + d2max * km.Sd;                                        // (5.3.4.1)
        if (kmLog is not null) uslLog = Math.Exp(kmLog.Mean + d2max * kmLog.Sd);           // (5.3.4.2)
        if (kmCbrt is not null) uslWh = Math.Max(0.0, Math.Pow(kmCbrt.Mean + d2max * kmCbrt.Sd, 3.0));  // (5.3.4.3 WH)
        if (kmRoot4 is not null) uslHw = Math.Pow(kmRoot4.Mean + d2max * kmRoot4.Sd, 4.0);              // (5.3.4.3 HW)
        double uslNonparam = observations.Max(o => o.Value);

        return new BtvNdResult
        {
            N = n, Nondetects = observations.Count(o => o.IsNondetect),
            ConfidenceCoefficient = cc, Coverage = coverage, FutureK = futureK,
            KmMean = km.Mean, KmSd = km.Sd,
            UplNormal1 = uplN1, UplNormalK = uplNk, UplNormalKMean = uplNkMean,
            UplChebyshev = uplCheby,
            LognormalApplicable = allPositive && !double.IsNaN(uplLog),
            UplLognormal1 = uplLog,
            GammaApplicable = allPositive,
            UplGammaWh = uplWh, UplGammaHw = uplHw,
            PercentileNormal = pctNormal, PercentileLognormal = pctLog,
            PercentileGammaWh = pctWh, PercentileGammaHw = pctHw,
            ToleranceFactorK = kFactor, UtlNormal = utlNormal, UtlLognormal = utlLog,
            UtlGammaWh = utlWh, UtlGammaHw = utlHw,
            D2Max = d2max, UslNormal = uslNormal, UslLognormal = uslLog,
            UslGammaWh = uslWh, UslGammaHw = uslHw, UslNonparametric = uslNonparam,
        };
    }

    /// <summary>
    /// KM estimates on a transformed copy of the data: every observation value
    /// (NDs carry their DL) is transformed; the ND flags are preserved
    /// (TG 5.3.x: "all detects and NDs are transformed to y-space").
    /// </summary>
    private static KmEstimates KmTransformed(IReadOnlyList<ValueObservation> observations,
        Func<double, double> transform)
    {
        var transformed = observations.Select(o => transform(o.Value)).ToArray();
        if (transformed.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
            return new KmEstimates(double.NaN, double.NaN, double.NaN);
        try
        {
            return Censored.KaplanMeier(transformed.Select((v, i) =>
            {
                var o = observations[i];
                return o.IsNondetect ? ValueObservation.Nondetect(v) : ValueObservation.Detect(v);
            }).ToList());
        }
        catch (ArgumentException)
        {
            return new KmEstimates(double.NaN, double.NaN, double.NaN);
        }
    }
}
