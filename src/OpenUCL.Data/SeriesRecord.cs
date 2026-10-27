using OpenUCL.Core;

namespace OpenUCL.Data;

/// <summary>One series (e.g. one monitoring point × one parameter) with its observations.</summary>
/// <param name="PointId">Identifier of the point / location the series belongs to.</param>
/// <param name="Parameter">Parameter / analyte the series refers to.</param>
/// <param name="Observations">Valid observations (detected values and nondetects).</param>
/// <param name="SkippedTokens">Number of packed tokens skipped as missing (empty, "NA", "ND", ...).</param>
public sealed record SeriesRecord(
    string PointId,
    string Parameter,
    IReadOnlyList<ValueObservation> Observations,
    int SkippedTokens = 0)
{
    public int N => Observations.Count;
    public int NondetectCount
    {
        get
        {
            int c = 0;
            foreach (var o in Observations) if (o.IsNondetect) c++;
            return c;
        }
    }

    /// <summary>Detected values only, in file order.</summary>
    public double[] DetectedValues()
    {
        var list = new List<double>(Observations.Count);
        foreach (var o in Observations) if (!o.IsNondetect) list.Add(o.Value);
        return list.ToArray();
    }
}
