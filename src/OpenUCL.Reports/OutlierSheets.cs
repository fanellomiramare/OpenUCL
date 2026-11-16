using System.Globalization;
using OpenUCL.Core;

namespace OpenUCL.Reports;

/// <summary>
/// Output sheet builders for the outlier modules, shared by the CLI and the GUI.
/// The same ReportSheet model guarantees identical numbers and layout across
/// front-ends (CLI text, GUI preview, exports).
/// </summary>
public static class OutlierSheets
{
    /// <summary>
    /// Builds the outlier sheet for one detected data set: Dixon extreme value
    /// test (n ≤ 25, exact critical values) and/or Rosner generalized ESD test
    /// (n ≥ 25). Algorithm warnings (e.g., Rosner on a degenerate sample) are
    /// returned in <paramref name="Warnings"/>; the caller decides how to
    /// surface them. The summary dictionary carries the machine-readable cells
    /// used by batch summaries and exports.
    /// </summary>
    public static (ReportSheet Sheet, Dictionary<string, string> Summary, List<string> Warnings) Build(
        double[] detected, double alpha, int r0, string version, string pointId, string parameter, int ndCount)
    {
        var warnings = new List<string>();
        var sheet = new ReportSheet($"OpenUCL {version} — OUTLIER TESTS (alpha = {alpha:G2})");
        sheet.Header.Add(("series", $"{pointId} / {parameter}    n (detects) = {detected.Length}    ND = {ndCount}"));
        sheet.Header.Add(("generated", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)));
        sheet.AddSection("notes")
            .AddNote("outlier tests are exploratory: confirm flagged values with site knowledge before exclusion (TG section 7.2).")
            .AddNote("both tests assume normality of the data without the suspected outliers.");

        string dLow = "", dLowOut = "", dHigh = "", dHighOut = "";
        if (detected.Length is >= 3 and <= 25)
        {
            var d = Outliers.Dixon(detected);
            string crit = $"10% {F(d.Critical10)} | 5% {F(d.Critical5)} | 1% {F(d.Critical1)}";
            var sec = sheet.AddSection("dixon extreme value test (n <= 25)");
            sec.ColumnsAre("tail", "observation", "statistic", "variant", "critical (10/5/1%)", "outlier");
            string lowFlag = (d.LowOutlier1, d.LowOutlier5, d.LowOutlier10) switch
            {
                (_, _, true) or (_, true, _) or (true, _, _) => "yes",
                _ => "no",
            };
            sec.AddRow("low", F(d.LowValue), F(d.LowStatistic), d.Variant, crit, lowFlag);
            string highFlag = (d.HighOutlier1, d.HighOutlier5, d.HighOutlier10) switch
            {
                (true, _, _) => "yes (1%)",
                (_, true, _) => "yes (5%)",
                (_, _, true) => "yes (10%)",
                _ => "no",
            };
            sec.AddRow("high", F(d.HighValue), F(d.HighStatistic), d.Variant, crit, highFlag);
            sec.AddNote("exact critical values computed by numerical integration of the order-statistic ratio distribution.");
            dLow = F(d.LowStatistic);
            dLowOut = lowFlag;
            dHigh = F(d.HighStatistic);
            dHighOut = highFlag;
        }

        string out5 = "", out1 = "", firstOutlier = "";
        if (detected.Length >= 25)
        {
            try
            {
                var ros = Outliers.Rosner(detected, r0);
                int out5v = ros.OutliersAt(0.05);
                int out1v = ros.OutliersAt(0.01);
                var sec = sheet.AddSection($"rosner test (n >= 25, up to {ros.MaxOutliers} outliers)");
                sec.ColumnsAre("#", "remaining", "mean", "sd", "potential outlier", "R", "crit 5%", "crit 1%");
                foreach (var st in ros.Steps)
                    sec.AddRow(st.Stage.ToString(), st.Remaining.ToString(), F(st.Mean), F(st.Sd),
                        F(st.Value), F(st.R), F(st.Critical5), F(st.Critical1));
                sec.AddNote($"at 5%: {out5v} potential outliers; at 1%: {out1v} potential outliers " +
                            "(critical values: generalized ESD formulation, within ~3% of the Rosner tables).");
                out5 = out5v.ToString(CultureInfo.InvariantCulture);
                out1 = out1v.ToString(CultureInfo.InvariantCulture);
                firstOutlier = out5v > 0 ? F(ros.Steps[0].Value) : "";
            }
            catch (ArgumentException ex) { warnings.Add($"Rosner: {ex.Message}"); }
        }

        var summary = new Dictionary<string, string>
        {
            ["dixon_low_statistic"] = dLow,
            ["dixon_low_outlier"] = dLowOut,
            ["dixon_high_statistic"] = dHigh,
            ["dixon_high_outlier"] = dHighOut,
            ["rosner_outliers_5pct"] = out5,
            ["rosner_outliers_1pct"] = out1,
            ["rosner_first_outlier"] = firstOutlier,
        };
        return (sheet, summary, warnings);
    }

    private static string F(double? v) => v is double d && !double.IsNaN(d)
        ? d.ToString("G6", CultureInfo.InvariantCulture) : "-";
}
