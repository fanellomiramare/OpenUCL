namespace OpenUCL.Core;

/// <summary>A UCL method that the ProUCL 5.2 decision logic can suggest (TG Appendix C).</summary>
public enum UclMethod
{
    StudentT,
    AdjustedGamma,
    ApproximateGamma,
    HUcl,
    KmT,
    KmBca,
    KmBootstrapT,
    KmHUcl,
    KmAdjustedGamma,
    KmApproximateGamma,
    GrosAdjustedGamma,
    GrosApproximateGamma,
}

/// <summary>One suggested UCL: method, display label and value.</summary>
public sealed record UclSuggestion(UclMethod Method, string Label, double Value);

/// <summary>
/// Distribution conclusions at the significance levels FIXED by the ProUCL 5.2
/// decision logic (TG Appendix C, Figure C-1): normal at alpha = 0.01, gamma at
/// 0.05, lognormal at 0.10 — "X or approximately X" = at least one of the two
/// GOF tests accepts. Computed on the detected values only (with NDs, ProUCL
/// runs the GOF tests on the detected observations).
/// </summary>
public sealed record UclDecisionGof(
    int DetectedN,
    bool Normal,
    bool Gamma,
    bool Lognormal,
    double KHat,     // gamma MLE shape of the detected data (NaN when not estimable)
    double KStar,    // bias-corrected shape (TG eq. 2-29)
    double SdLog);   // sd of ln(detected) (NaN when a value is <= 0)

/// <summary>Candidate UCL values the decision can pick from (NaN = not computed).</summary>
public sealed record UclCandidates
{
    public double StudentT { get; init; } = double.NaN;
    public double AdjustedGamma { get; init; } = double.NaN;
    public double ApproximateGamma { get; init; } = double.NaN;
    public double HUcl { get; init; } = double.NaN;
    public double KmT { get; init; } = double.NaN;
    public double KmBca { get; init; } = double.NaN;
    public double KmBootstrapT { get; init; } = double.NaN;
    public double KmHUcl { get; init; } = double.NaN;
    public double KmAdjustedGamma { get; init; } = double.NaN;
    public double KmApproximateGamma { get; init; } = double.NaN;
    public double GrosAdjustedGamma { get; init; } = double.NaN;
    public double GrosApproximateGamma { get; init; } = double.NaN;
}

/// <summary>
/// The outcome of the decision: zero, one or two suggestions (ProUCL may
/// suggest two UCLs, e.g. "KM adjusted gamma or GROS adjusted gamma"), the
/// distribution branch taken and a printable rationale.
/// </summary>
public sealed record UclRecommendation(
    IReadOnlyList<UclSuggestion> Suggestions,
    string Distribution,
    string Rationale)
{
    public UclSuggestion? Primary => Suggestions.Count > 0 ? Suggestions[0] : null;
}

/// <summary>
/// The ProUCL 5.2 UCL recommendation logic, transcribed from the decision
/// flowcharts of TG 5.2 Appendix C (Figures C-1..C-5):
/// <list type="bullet">
/// <item>C-1: normal (0.01) → Student's t / KM (t); otherwise n &lt; 10 → none;
/// gamma (0.05) → C-2/C-4; lognormal (0.10) → C-3/C-5; else Student's t / KM (t)
/// (TG 2.5.2.4: nonparametric data → Student's t).</item>
/// <item>C-2 (gamma, no NDs): k-hat &lt; 0.5 or n &lt; 50 → adjusted gamma,
/// otherwise approximate gamma; a 0, negative or NA value → none.</item>
/// <item>C-3 (lognormal, no NDs): n ≥ 28 → H-UCL unless the H flag (n &lt; 3,
/// n &gt; 1001, values ≤ 0); n &lt; 28 → H-UCL when sd(log) &lt; 1.5, else Student's t.</item>
/// <item>C-4 (gamma, NDs; k* of the detects): k* &gt; 1 → KM/GROS adjusted gamma
/// (n &lt; 50) or KM/GROS approximate gamma; k* ≤ 1 → KM bootstrap-t or KM H-UCL
/// (n &lt; 15), KM adjusted gamma (15 ≤ n &lt; 50), KM approximate gamma (n ≥ 50).</item>
/// <item>C-5 (lognormal, NDs): n ≥ 75 → KM H-UCL; otherwise sd(log of detects)
/// &lt; 1.5 → KM (t) or KM BCA, else none.</item>
/// </list>
/// ProUCL makes recommendations only for 95% UCLs (User Guide 5.2, UCL module):
/// at any other confidence level the distribution is still classified but no
/// UCL is suggested.
/// </summary>
public static class UclDecision
{
    public const double NormalAlpha = 0.01;
    public const double GammaAlpha = 0.05;
    public const double LognormalAlpha = 0.10;

    /// <summary>GOF conclusions on the detected values at the fixed decision levels.</summary>
    public static UclDecisionGof EvaluateGof(ReadOnlySpan<double> detected)
    {
        double[] x = detected.ToArray();
        int n = x.Length;
        bool normal = NormalOrApproximate(x, NormalAlpha);

        bool positive = n > 0 && x.All(v => v > 0);
        bool gamma = false;
        double kHat = double.NaN, kStar = double.NaN;
        if (positive && n >= 3)
        {
            try
            {
                var fit = GammaDistribution.FitMLE(x);
                kHat = fit.KHat;
                kStar = fit.KStar;
                var g = GammaGof.Compute(x);
                gamma = g.AD_A2 <= g.CriticalValue(GammaGofTest.AndersonDarling, GammaAlpha)
                     || g.KS_D <= g.CriticalValue(GammaGofTest.KolmogorovSmirnov, GammaAlpha);
            }
            catch (ArgumentException) { /* shape not estimable: not gamma */ }
        }

        double sdLog = double.NaN;
        bool lognormal = false;
        if (positive && n >= 2)
        {
            var logs = x.Select(v => Math.Log(v)).ToArray();
            double m = logs.Average();
            sdLog = Math.Sqrt(logs.Sum(v => (v - m) * (v - m)) / (n - 1));
            lognormal = NormalOrApproximate(logs, LognormalAlpha);
        }
        return new UclDecisionGof(n, normal, gamma, lognormal, kHat, kStar, sdLog);
    }

    /// <summary>
    /// "Normal or approximately normal" at alpha: Shapiro-Wilk (only up to
    /// <see cref="GoodnessOfFit.ShapiroWilkProUclMaxN"/>) and Lilliefors, one
    /// passing test suffices. Identical values cannot be classified (false).
    /// </summary>
    public static bool NormalOrApproximate(double[] x, double alpha)
    {
        if (x.Length < 3) return false;
        try
        {
            ShapiroWilkResult? sw = x.Length <= GoodnessOfFit.ShapiroWilkProUclMaxN
                ? GoodnessOfFit.ShapiroWilk(x) : null;
            var lf = GoodnessOfFit.Lilliefors(x);
            return GoodnessOfFit.Conclude(sw, lf, alpha) != GofConclusion.NotNormal;
        }
        catch (ArgumentException) { return false; }
    }

    /// <summary>Applies the Appendix C flowcharts. n = total sample size (detects + NDs).</summary>
    public static UclRecommendation Recommend(double cc, int n, bool hasNondetects,
        UclDecisionGof gof, UclCandidates c)
    {
        string normalTxt = $"normal at alpha {NormalAlpha:G2}";
        string gammaTxt = $"gamma at {GammaAlpha:G2}";
        string lognTxt = $"lognormal at {LognormalAlpha:G2}";
        string data = hasNondetects ? "detected data" : "data";

        string branch;
        Func<UclRecommendation> decide;
        if (gof.Normal)
        {
            branch = "normal";
            decide = hasNondetects
                ? () => One(branch, UclMethod.KmT, "KM (t)", c.KmT,
                    $"{data} normal or approximately normal at alpha {NormalAlpha:G2}: KM (t) UCL (TG Figure C-1).", positiveOnly: false)
                : () => One(branch, UclMethod.StudentT, "Student's t", c.StudentT,
                    $"{data} normal or approximately normal at alpha {NormalAlpha:G2}: Student's t UCL (TG 2.5.2.1, Figure C-1).", positiveOnly: false);
        }
        else if (n < 10)
        {
            branch = "undetermined";
            decide = () => None(branch,
                $"{data} not {normalTxt} and n = {n} < 10: no recommendation (TG Figure C-1).");
        }
        else if (gof.Gamma)
        {
            branch = "gamma";
            decide = hasNondetects ? () => GammaWithNd(n, gof, c) : () => GammaNoNd(n, gof, c);
        }
        else if (gof.Lognormal)
        {
            branch = "lognormal";
            decide = hasNondetects ? () => LognormalWithNd(n, gof, c) : () => LognormalNoNd(n, gof, c);
        }
        else
        {
            branch = "nonparametric";
            string why = $"{data} not {normalTxt}, not {gammaTxt}, not {lognTxt}";
            decide = hasNondetects
                ? () => One(branch, UclMethod.KmT, "KM (t)", c.KmT,
                    $"{why}: KM (t) UCL (TG Figure C-1).", positiveOnly: false)
                : () => One(branch, UclMethod.StudentT, "Student's t", c.StudentT,
                    $"{why}: Student's t UCL (TG 2.5.2.4, Figure C-1).", positiveOnly: false);
        }

        if (Math.Abs(cc - 0.95) > 1e-9)
            return None(branch,
                $"ProUCL 5.2 suggests UCLs only at 95% confidence (User Guide, UCL module); " +
                $"the {cc * 100:G4}% UCLs are all listed above (distribution branch: {branch}).");
        return decide();
    }

    // Figure C-2: gamma, no NDs (k-hat = MLE of the shape).
    private static UclRecommendation GammaNoNd(int n, UclDecisionGof gof, UclCandidates c)
    {
        const string branch = "gamma";
        if (double.IsNaN(gof.KHat))
            return None(branch, "gamma shape k-hat not available: no recommendation (TG Figure C-2).");
        string path = $"data gamma or approximately gamma at alpha {GammaAlpha:G2} (not normal at {NormalAlpha:G2}); " +
                      $"k-hat = {Fmt(gof.KHat)}, n = {n}";
        if (gof.KHat < 0.5 || n < 50)
            return Valid(c.AdjustedGamma)
                ? One(branch, UclMethod.AdjustedGamma, "adjusted gamma", c.AdjustedGamma,
                    $"{path} ({(gof.KHat < 0.5 ? "k-hat < 0.5" : "n < 50")}): adjusted gamma UCL (TG Figure C-2).")
                : None(branch, $"{path}: the adjusted gamma UCL is 0, negative or NA — no recommendation (TG Figure C-2).");
        return Valid(c.ApproximateGamma)
            ? One(branch, UclMethod.ApproximateGamma, "approximate gamma", c.ApproximateGamma,
                $"{path} (k-hat >= 0.5 and n >= 50): approximate gamma UCL (TG Figure C-2).")
            : None(branch, $"{path}: the approximate gamma UCL is 0, negative or NA — no recommendation (TG Figure C-2).");
    }

    // Figure C-3: lognormal, no NDs (H flag: n < 3, n > 1001, any value <= 0).
    private static UclRecommendation LognormalNoNd(int n, UclDecisionGof gof, UclCandidates c)
    {
        const string branch = "lognormal";
        string path = $"data lognormal or approximately lognormal at alpha {LognormalAlpha:G2} " +
                      $"(not normal at {NormalAlpha:G2}, not gamma at {GammaAlpha:G2}); n = {n}";
        if (n >= 28)
        {
            if (n > 1001)
                return None(branch, $"{path}: H flag (n > 1001) — no recommendation (TG Figure C-3); the exact H-UCL is listed above.");
            return Valid(c.HUcl)
                ? One(branch, UclMethod.HUcl, "H-UCL", c.HUcl, $"{path} >= 28: H-UCL (TG 2.5.2.3, Figure C-3).")
                : None(branch, $"{path}: H-UCL not available — no recommendation (TG Figure C-3).");
        }
        if (double.IsNaN(gof.SdLog))
            return None(branch, $"{path}: log-transformed data not available — no recommendation (TG Figure C-3).");
        string sd = $"sd(log) = {Fmt(gof.SdLog)}";
        if (gof.SdLog < 1.5)
            return Valid(c.HUcl)
                ? One(branch, UclMethod.HUcl, "H-UCL", c.HUcl, $"{path} < 28 and {sd} < 1.5: H-UCL (TG Table 2-13, Figure C-3).")
                : None(branch, $"{path}: H-UCL not available — no recommendation (TG Figure C-3).");
        return One(branch, UclMethod.StudentT, "Student's t", c.StudentT,
            $"{path} < 28 and {sd} >= 1.5: Student's t UCL (TG Table 2-13, Figure C-3).", positiveOnly: false);
    }

    // Figure C-4: gamma with NDs (k* of the detects).
    private static UclRecommendation GammaWithNd(int n, UclDecisionGof gof, UclCandidates c)
    {
        const string branch = "gamma";
        if (double.IsNaN(gof.KStar))
            return None(branch, "gamma shape k* of the detects not available: no recommendation (TG Figure C-4).");
        string path = $"detected data gamma or approximately gamma at alpha {GammaAlpha:G2} (not normal at {NormalAlpha:G2}); " +
                      $"k* (detects) = {Fmt(gof.KStar)}, n = {n}";
        var list = new List<UclSuggestion>();
        string rule;
        if (gof.KStar > 1)
        {
            if (n < 50)
            {
                if (Valid(c.KmAdjustedGamma)) list.Add(new(UclMethod.KmAdjustedGamma, "KM adjusted gamma", c.KmAdjustedGamma));
                if (Valid(c.GrosAdjustedGamma)) list.Add(new(UclMethod.GrosAdjustedGamma, "GROS adjusted gamma", c.GrosAdjustedGamma));
                rule = "k* > 1 and n < 50: KM adjusted gamma or GROS adjusted gamma UCL";
            }
            else
            {
                if (Valid(c.KmApproximateGamma)) list.Add(new(UclMethod.KmApproximateGamma, "KM approximate gamma", c.KmApproximateGamma));
                if (Valid(c.GrosApproximateGamma)) list.Add(new(UclMethod.GrosApproximateGamma, "GROS approximate gamma", c.GrosApproximateGamma));
                rule = "k* > 1 and n >= 50: KM approximate gamma or GROS approximate gamma UCL";
            }
        }
        else if (n < 15)
        {
            if (Valid(c.KmBootstrapT)) list.Add(new(UclMethod.KmBootstrapT, "KM bootstrap-t", c.KmBootstrapT));
            if (Valid(c.KmHUcl)) list.Add(new(UclMethod.KmHUcl, "KM H-UCL", c.KmHUcl));
            rule = "k* <= 1 and n < 15: KM bootstrap-t or KM H-UCL (if they look erratic or inflated, " +
                   "use an adjusted gamma UCL — TG Figure C-4 note)";
        }
        else if (n < 50)
        {
            if (Valid(c.KmAdjustedGamma)) list.Add(new(UclMethod.KmAdjustedGamma, "KM adjusted gamma", c.KmAdjustedGamma));
            else if (Valid(c.KmApproximateGamma)) list.Add(new(UclMethod.KmApproximateGamma, "KM approximate gamma", c.KmApproximateGamma));
            rule = "k* <= 1 and 15 <= n < 50: KM adjusted gamma UCL (KM approximate gamma when the adjusted is NA)";
        }
        else
        {
            if (Valid(c.KmApproximateGamma)) list.Add(new(UclMethod.KmApproximateGamma, "KM approximate gamma", c.KmApproximateGamma));
            rule = "k* <= 1 and n >= 50: KM approximate gamma UCL";
        }
        return list.Count > 0
            ? new UclRecommendation(list, branch, $"{path}; {rule} (TG Figure C-4).")
            : None(branch, $"{path}; {rule}, but none is available — no recommendation (TG Figure C-4).");
    }

    // Figure C-5: lognormal with NDs.
    private static UclRecommendation LognormalWithNd(int n, UclDecisionGof gof, UclCandidates c)
    {
        const string branch = "lognormal";
        string path = $"detected data lognormal or approximately lognormal at alpha {LognormalAlpha:G2} " +
                      $"(not normal at {NormalAlpha:G2}, not gamma at {GammaAlpha:G2}); n = {n}";
        if (n >= 75)
            return Valid(c.KmHUcl)
                ? One(branch, UclMethod.KmHUcl, "KM H-UCL", c.KmHUcl, $"{path} >= 75: KM H-UCL (TG Figure C-5).")
                : None(branch, $"{path} >= 75: KM H-UCL not available — no recommendation (TG Figure C-5).");
        if (double.IsNaN(gof.SdLog))
            return None(branch, $"{path}: sd of the logged detects not available — no recommendation (TG Figure C-5).");
        string sd = $"sd(log detects) = {Fmt(gof.SdLog)}";
        if (gof.SdLog >= 1.5)
            return None(branch, $"{path} < 75 and {sd} >= 1.5: no recommendation (TG Figure C-5).");
        var list = new List<UclSuggestion>();
        if (Valid(c.KmT)) list.Add(new(UclMethod.KmT, "KM (t)", c.KmT));
        if (Valid(c.KmBca)) list.Add(new(UclMethod.KmBca, "KM BCA bootstrap", c.KmBca));
        return list.Count > 0
            ? new UclRecommendation(list, branch, $"{path} < 75 and {sd} < 1.5: KM (t) or KM BCA UCL (TG Figure C-5).")
            : None(branch, $"{path}: KM (t) and KM BCA not available — no recommendation (TG Figure C-5).");
    }

    private static bool Valid(double v) => !double.IsNaN(v) && v > 0;

    // Gamma/H/KM-gamma suggestions require a positive value (the flowcharts'
    // "0, negative, or NA" checks); Student's t and KM (t) only a finite one
    // (data may legitimately be negative, e.g. differences).
    private static UclRecommendation One(string branch, UclMethod m, string label, double value, string rationale,
        bool positiveOnly = true) =>
        (positiveOnly ? Valid(value) : double.IsFinite(value))
            ? new UclRecommendation(new[] { new UclSuggestion(m, label, value) }, branch, rationale)
            : None(branch, rationale + $" ({label} not available — no recommendation)");

    private static UclRecommendation None(string branch, string rationale) =>
        new(Array.Empty<UclSuggestion>(), branch, rationale);

    private static string Fmt(double v) => v.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
}
