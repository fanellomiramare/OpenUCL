namespace OpenUCL.Data;

/// <summary>A single observation of a series: a detected value, or a nondetect known by its detection limit.</summary>
public readonly record struct ValueObservation(double Value, bool IsNondetect)
{
    public static ValueObservation Detect(double value) => new(value, IsNondetect: false);

    /// <summary>A nondetect reported as "&lt;DL"; <see cref="Value"/> carries the detection/reporting limit.</summary>
    public static ValueObservation Nondetect(double detectionLimit) => new(detectionLimit, IsNondetect: true);

    public override string ToString() => IsNondetect ? $"<{Value}" : Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
}

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
