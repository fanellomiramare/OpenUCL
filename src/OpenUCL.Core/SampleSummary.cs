namespace OpenUCL.Core;

/// <summary>
/// Descriptive statistics for a single univariate sample.
/// All values are computed on the full-precision input; percentiles use the
/// linear interpolation between order statistics (R type 7 / NumPy default).
/// </summary>
/// <param name="N">Number of observations.</param>
/// <param name="Min">Smallest observation.</param>
/// <param name="Max">Largest observation.</param>
/// <param name="Mean">Arithmetic mean.</param>
/// <param name="Variance">Unbiased sample variance (n-1 denominator); NaN when n &lt; 2.</param>
/// <param name="StandardDeviation">Sample standard deviation; NaN when n &lt; 2.</param>
/// <param name="Median">50th percentile.</param>
/// <param name="Q1">25th percentile.</param>
/// <param name="Q3">75th percentile.</param>
public sealed record SampleSummary(
    int N,
    double Min,
    double Max,
    double Mean,
    double Variance,
    double StandardDeviation,
    double Median,
    double Q1,
    double Q3)
{
    /// <summary>Coefficient of variation (SD / mean); NaN when the mean is zero or SD is NaN.</summary>
    public double CoefficientOfVariation =>
        Mean == 0 || double.IsNaN(StandardDeviation) ? double.NaN : StandardDeviation / Mean;
}

public static class Descriptive
{
    /// <summary>Computes descriptive statistics. Throws <see cref="ArgumentException"/> on an empty sample.</summary>
    public static SampleSummary Compute(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        int n = values.Count;
        if (n == 0)
            throw new ArgumentException("Cannot compute statistics of an empty sample.", nameof(values));

        double min = double.PositiveInfinity, max = double.NegativeInfinity, sum = 0;
        for (int i = 0; i < n; i++)
        {
            double v = values[i];
            if (v < min) min = v;
            if (v > max) max = v;
            sum += v;
        }
        double mean = sum / n;

        double variance = double.NaN;
        if (n > 1)
        {
            double sq = 0;
            for (int i = 0; i < n; i++)
            {
                double d = values[i] - mean;
                sq += d * d;
            }
            variance = sq / (n - 1);
        }

        double[] sorted = new double[n];
        for (int i = 0; i < n; i++) sorted[i] = values[i];
        Array.Sort(sorted);

        return new SampleSummary(
            N: n,
            Min: min,
            Max: max,
            Mean: mean,
            Variance: variance,
            StandardDeviation: Math.Sqrt(variance),
            Median: Percentile(sorted, 0.5),
            Q1: Percentile(sorted, 0.25),
            Q3: Percentile(sorted, 0.75));
    }

    /// <summary>Linear-interpolation percentile (type 7) on an already sorted array.</summary>
    public static double Percentile(double[] sorted, double p)
    {
        double h = (sorted.Length - 1) * p;
        int lo = (int)Math.Floor(h);
        int hi = Math.Min(lo + 1, sorted.Length - 1);
        return sorted[lo] + (h - lo) * (sorted[hi] - sorted[lo]);
    }
}
