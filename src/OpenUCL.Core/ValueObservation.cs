namespace OpenUCL.Core;

/// <summary>A single observation of a series: a detected value, or a nondetect known by its detection limit.</summary>
public readonly record struct ValueObservation(double Value, bool IsNondetect)
{
    public static ValueObservation Detect(double value) => new(value, IsNondetect: false);

    /// <summary>A nondetect reported as "&lt;DL"; <see cref="Value"/> carries the detection/reporting limit.</summary>
    public static ValueObservation Nondetect(double detectionLimit) => new(detectionLimit, IsNondetect: true);

    public override string ToString() => IsNondetect ? $"<{Value}" : Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
}
