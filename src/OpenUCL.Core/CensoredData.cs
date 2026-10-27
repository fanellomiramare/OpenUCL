namespace OpenUCL.Core;

/// <summary>Kaplan-Meier (product limit) estimates for a left-censored data set.</summary>
public sealed record KmEstimates(double Mean, double StandardErrorOfMean, double Sd);

/// <summary>Result of a robust ROS imputation (ProUCL LROS / GROS).</summary>
public sealed record RosResult(
    double[] FullData,           // detected observations + imputed NDs (n values)
    double[] ImputedValues,      // the k imputed ND values, in ascending-DL order of the NDs
    double Intercept,            // OLS intercept on the modelled scale
    double Slope,                // OLS slope
    bool NegativeImputations);   // true when some imputed NDs came out <= 0 (known GROS issue)

/// <summary>
/// Estimation methods for left-censored data sets (nondetects), following
/// ProUCL Technical Guide 5.2 chapter 4: Kaplan-Meier (§4.4, eq. 4-1..4-3),
/// robust ROS for lognormal (LROS) and gamma (GROS) distributions (§4.5) with
/// Helsel-Cohn / Hirsch-Stedinger product-limit plotting positions for multiple
/// detection limits; Blom positions for the single-DL normal/lognormal case and
/// empirical (i-1/2)/n positions for gamma; and DL/2 substitution.
/// </summary>
public static class Censored
{
    /// <summary>Kaplan-Meier mean, SE of the mean and direct sd (TG eq. 4-1, 4-2, 4-3).</summary>
    public static KmEstimates KaplanMeier(IReadOnlyList<ValueObservation> observations)
    {
        int n = observations.Count;
        if (n == 0) throw new ArgumentException("Empty data set.", nameof(observations));
        int k = 0;
        foreach (var o in observations) if (o.IsNondetect) k++;
        if (k == n) throw new ArgumentException("All observations are nondetects: KM is undefined.", nameof(observations));

        var sorted = observations.OrderBy(o => o.Value).ThenBy(o => o.IsNondetect ? 0 : 1).ToList();

        // Distinct detected values ascending, with m_j (detects at x'_j)
        // and n_j (all observations, NDs included, with value <= x'_j).
        var xs = new List<double>();
        var ms = new List<int>();
        foreach (var o in sorted)
        {
            if (o.IsNondetect) continue;
            if (xs.Count > 0 && xs[^1] == o.Value) { ms[^1]++; continue; }
            xs.Add(o.Value);
            ms.Add(1);
        }
        int np = xs.Count;
        var ns = new int[np];
        int idx = 0;
        for (int j = 0; j < np; j++)
        {
            while (idx < n && sorted[idx].Value <= xs[j] + 1e-12) idx++;
            ns[j] = idx;
        }

        // KM CDF at each distinct detected value (TG §4.4):
        // F(x) = prod over detected values x'_h > x of (n_h - m_h)/n_h, F(x'_np) = 1.
        var f = new double[np];
        f[np - 1] = 1.0;
        for (int j = np - 2; j >= 0; j--)
            f[j] = f[j + 1] * (ns[j + 1] - ms[j + 1]) / (double)ns[j + 1];

        // Eq. 4-1 and eq. 4-3.
        double mean = 0, meanSq = 0, prevF = 0;
        for (int j = 0; j < np; j++)
        {
            mean += xs[j] * (f[j] - prevF);
            meanSq += xs[j] * xs[j] * (f[j] - prevF);
            prevF = f[j];
        }
        double variance = meanSq - mean * mean;

        // Eq. 4-2: SE of the mean.
        double se = double.NaN;
        if (n - k > 1 && np > 1)
        {
            var alpha = new double[np];
            for (int i = 1; i < np; i++)
                alpha[i] = alpha[i - 1] + (xs[i] - xs[i - 1]) * f[i - 1];

            double sum = 0;
            for (int i = 1; i < np; i++)
            {
                double m2 = ms[i], n2 = ns[i];
                sum += alpha[i - 1] * alpha[i - 1] * m2 * m2 / (n2 * n2 * (n2 - m2));
            }
            se = Math.Sqrt((n - k) / (double)(n - k - 1) * sum);
        }

        return new KmEstimates(mean, se, Math.Sqrt(Math.Max(variance, 0)));
    }

    /// <summary>DL/2 substitution: NDs replaced by half their detection limit.</summary>
    public static double[] DlHalf(IReadOnlyList<ValueObservation> observations)
    {
        var data = new double[observations.Count];
        for (int i = 0; i < observations.Count; i++)
        {
            var o = observations[i];
            data[i] = o.IsNondetect ? o.Value / 2.0 : o.Value;
        }
        return data;
    }

    /// <summary>
    /// Robust ROS for a lognormal distribution (ProUCL LROS): OLS regression of
    /// log-transformed detects on normal quantiles; imputed log-NDs are exponentiated.
    /// </summary>
    public static RosResult RosLognormal(IReadOnlyList<ValueObservation> observations) =>
        Ros(observations, logScale: true);

    /// <summary>
    /// Robust ROS for a gamma distribution (ProUCL GROS): OLS regression of raw detects
    /// on gamma quantiles (MLE shape from detected data). May yield negative imputations
    /// for skewed data with outliers — a documented limitation of GROS.
    /// </summary>
    public static RosResult RosGamma(IReadOnlyList<ValueObservation> observations) =>
        Ros(observations, logScale: false);

    private static RosResult Ros(IReadOnlyList<ValueObservation> observations, bool logScale)
    {
        int n = observations.Count;
        var detects = new List<double>();
        foreach (var o in observations) if (!o.IsNondetect) detects.Add(o.Value);
        if (detects.Count < 2)
            throw new ArgumentException("ROS requires at least 2 detected observations.", nameof(observations));

        var sorted = observations.OrderBy(o => o.Value).ThenBy(o => o.IsNondetect ? 0 : 1).ToList();
        var positions = PlottingPositions(observations, gamma: !logScale);

        double kHat = double.NaN, thetaHat = double.NaN;
        if (!logScale)
        {
            var fit = GammaDistribution.FitMLE(detects.ToArray());
            kHat = fit.KHat;
            thetaHat = fit.ThetaHat;
        }

        var qDetect = new List<double>();
        var qNd = new List<double>();
        for (int i = 0; i < n; i++)
        {
            double q = logScale
                ? NormalDistribution.Quantile(positions[i])
                : GammaDistribution.Quantile(positions[i], kHat, thetaHat);
            if (sorted[i].IsNondetect) qNd.Add(q);
            else qDetect.Add(q);
        }

        var yDetect = detects.Select(v => logScale ? Math.Log(v) : v).ToArray();
        (double a, double b) = Ols(qDetect.ToArray(), yDetect);

        var imputed = qNd.Select(q => a + b * q).ToArray();
        if (logScale) imputed = imputed.Select(Math.Exp).ToArray();

        var full = new List<double>(detects);
        full.AddRange(imputed);
        return new RosResult(full.ToArray(), imputed, a, b, imputed.Any(v => v <= 0));
    }

    /// <summary>
    /// Plotting positions for all n observations, in the order of the observations
    /// sorted ascending by value (NDs before detects on ties).
    /// - Single DL with all NDs strictly below every detect: Blom (i-3/8)/(n+1/4) for
    ///   normal/lognormal, empirical (i-1/2)/n for gamma (ProUCL convention, TG §4.5.1).
    /// - Otherwise: Helsel-Cohn product-limit positions adjusted for the detection
    ///   limits (TG §4.5.1; Helsel 2005; Singh, Maichle, Lee 2006).
    /// </summary>
    internal static double[] PlottingPositions(IReadOnlyList<ValueObservation> observations, bool gamma)
    {
        int n = observations.Count;
        var sorted = observations
            .Select((o, i) => (o, i))
            .OrderBy(t => t.o.Value)
            .ThenBy(t => t.o.IsNondetect ? 0 : 1)
            .Select(t => t.o)
            .ToList();

        var thresholds = sorted.Where(o => o.IsNondetect).Select(o => o.Value).Distinct().OrderBy(v => v).ToList();
        int m = thresholds.Count;
        if (m == 0)
            throw new ArgumentException("No nondetects: censored-data plotting positions are not applicable.", nameof(observations));

        int totalNd = sorted.Count(o => o.IsNondetect);
        bool singleDlBelowAllDetects = m == 1 && totalNd < n && sorted[totalNd - 1].Value < sorted[totalNd].Value;

        var pp = new double[n];
        if (singleDlBelowAllDetects)
        {
            for (int i = 0; i < n; i++)
                pp[i] = gamma ? (i + 0.5) / n : (i + 0.625) / (n + 0.25);
            return pp;
        }

        // Helsel-Cohn (NADA ros / cohn()). Per-threshold counts (t_j, j = 1..m):
        //   A_j = detects in the stratum [t_j, t_{j+1})  (A_m = detects >= t_m; A_0 = detects < t_1)
        //   B_j = all observations <= t_j, excluding detects equal to t_j
        //   C_j = NDs at exactly t_j
        var a = new double[m + 1];
        var b = new double[m + 1];
        var c = new double[m + 1];
        foreach (var o in sorted)
        {
            if (o.IsNondetect)
            {
                int j = ThresholdIndex(thresholds, o.Value);
                c[j]++;
                for (int t = 1; t <= j; t++) b[t]++;
            }
            else
            {
                int j = 0;
                for (int t = 1; t <= m; t++)
                {
                    if (o.Value >= thresholds[t - 1]) j = t;
                    if (o.Value < thresholds[t - 1]) b[t]++;
                }
                a[j]++;
            }
        }

        // Exceedance probabilities, top threshold downward. p[m+1] = 0.
        var p = new double[m + 2];
        for (int j = m; j >= 1; j--)
        {
            double ratio = a[j] + b[j] > 0 ? a[j] / (a[j] + b[j]) : 1.0;
            p[j] = p[j + 1] + ratio * (1.0 - p[j + 1]);
        }

        // Walk the sorted observations assigning positions.
        var ndRankOfThreshold = new Dictionary<double, int>();
        var detectRankBelowThreshold = new Dictionary<int, int>(); // per block j: running R
        for (int i = 0; i < n; i++)
        {
            var o = sorted[i];
            if (o.IsNondetect)
            {
                int j = ThresholdIndex(thresholds, o.Value);
                int r = ndRankOfThreshold.TryGetValue(o.Value, out int v0) ? v0 + 1 : 1;
                ndRankOfThreshold[o.Value] = r;
                pp[i] = (1.0 - p[j]) * r / (c[j] + 1.0);
            }
            else
            {
                int j = 0;
                for (int t = 1; t <= m; t++) if (o.Value >= thresholds[t - 1]) j = t;
                int r = detectRankBelowThreshold.TryGetValue(j, out int v1) ? v1 + 1 : 1;
                detectRankBelowThreshold[j] = r;
                if (j == 0)
                {
                    pp[i] = (1.0 - p[1]) * r / (a[0] + 1.0);
                }
                else
                {
                    pp[i] = (1.0 - p[j]) + (p[j] - p[j + 1]) * r / (a[j] + 1.0);
                }
            }
        }
        return pp;
    }

    private static int ThresholdIndex(List<double> thresholds, double dl)
    {
        for (int i = 0; i < thresholds.Count; i++)
            if (thresholds[i] == dl) return i + 1;   // 1-based
        throw new ArgumentException($"Detection limit {dl} not found.");
    }

    internal static (double Intercept, double Slope) Ols(double[] x, double[] y)
    {
        int n = x.Length;
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        for (int i = 0; i < n; i++) { sx += x[i]; sy += y[i]; sxx += x[i] * x[i]; sxy += x[i] * y[i]; }
        double d = n * sxx - sx * sx;
        if (d == 0) throw new ArgumentException("Degenerate OLS: quantiles are all equal.");
        double slope = (n * sxy - sx * sy) / d;
        return (sy / n - slope * sx / n, slope);
    }
}
