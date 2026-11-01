namespace OpenUCL.Core;

/// <summary>Dixon's extreme value test result (both tails). TG 5.2 section 7.3.1.</summary>
public sealed record DixonResult
{
    public required int N { get; init; }
    /// <summary>Subrange variant actually used: r10 (n 3-7), r11 (8-10), r21 (11-13), r22 (14-25).</summary>
    public required string Variant { get; init; }
    public required double LowStatistic { get; init; }    // X(1) potential low outlier
    public required double HighStatistic { get; init; }   // X(n) potential high outlier
    public required double LowValue { get; init; }
    public required double HighValue { get; init; }
}

/// <summary>One step of Rosner's iterative outlier search (TG 5.2 section 7.3.2).</summary>
public sealed record RosnerStep
{
    public required int Stage { get; init; }        // i (1-based): testing the i-th most extreme value
    public required int Remaining { get; init; }    // observations still in the sample
    public required double Mean { get; init; }      // x-bar(i-1)
    public required double Sd { get; init; }        // s(i-1)
    public required double Value { get; init; }     // y(i-1): farthest observation from the mean
    public required double R { get; init; }         // |y - mean| / sd
    public required double Critical5 { get; init; } // lambda_i at 5% (generalized ESD)
    public required double Critical1 { get; init; } // lambda_i at 1%
}

public sealed record RosnerResult
{
    public required int N { get; init; }
    public required int MaxOutliers { get; init; }
    public required IReadOnlyList<RosnerStep> Steps { get; init; }

    /// <summary>Number of outliers concluded at the given alpha walking down from r0 (TG STEP 3).</summary>
    public int OutliersAt(double alpha)
    {
        for (int i = Steps.Count; i >= 1; i--)
        {
            var s = Steps[i - 1];
            double crit = alpha <= 0.01 ? s.Critical1 : s.Critical5;
            if (s.R >= crit) return i;
        }
        return 0;
    }
}

/// <summary>
/// Classical outlier tests: Dixon's extreme value test (n &lt;= 25, TG 7.3.1) and
/// Rosner's test for up to 10 outliers in n &gt;= 25 (TG 7.3.2), with critical
/// values from the generalized extreme Studentized deviate (ESD) formulation
/// (Rosner 1983; matches the TG tables within ~3%).
/// </summary>
public static class Outliers
{
    public const int RosnerMaxOutliers = 10;

    /// <summary>Dixon's test. Requires 3 &lt;= n &lt;= 25. Critical values are tabulated
    /// (Rorabacher 1991 / Gilbert 1987) and are not yet embedded; the statistics are exact.</summary>
    public static DixonResult Dixon(ReadOnlySpan<double> data)
    {
        int n = data.Length;
        if (n is < 3 or > 25)
            throw new ArgumentException("Dixon's test requires 3 <= n <= 25.", nameof(data));

        double[] x = data.ToArray();
        Array.Sort(x);

        string variant = n switch
        {
            <= 7 => "r10",
            <= 10 => "r11",
            <= 13 => "r21",
            _ => "r22",
        };

        // Case 1: X(1) low outlier.
        double low = n switch
        {
            <= 7 => (x[1] - x[0]) / (x[n - 1] - x[0]),
            <= 10 => (x[1] - x[0]) / (x[n - 2] - x[0]),
            <= 13 => (x[2] - x[0]) / (x[n - 2] - x[0]),
            _ => (x[2] - x[0]) / (x[n - 3] - x[0]),
        };

        // Case 2: X(n) high outlier.
        double high = n switch
        {
            <= 7 => (x[n - 1] - x[n - 2]) / (x[n - 1] - x[0]),
            <= 10 => (x[n - 1] - x[n - 2]) / (x[n - 1] - x[1]),
            <= 13 => (x[n - 1] - x[n - 3]) / (x[n - 1] - x[1]),
            _ => (x[n - 1] - x[n - 3]) / (x[n - 1] - x[2]),
        };

        return new DixonResult
        {
            N = n, Variant = variant,
            LowStatistic = low, LowValue = x[0],
            HighStatistic = high, HighValue = x[^1],
        };
    }

    /// <summary>Rosner's test. Requires n &gt;= 25; tests up to maxOutliers (default 10).</summary>
    public static RosnerResult Rosner(ReadOnlySpan<double> data, int maxOutliers = RosnerMaxOutliers)
    {
        int n = data.Length;
        if (n < 25) throw new ArgumentException("Rosner's test requires n >= 25.", nameof(data));
        int r0 = Math.Clamp(maxOutliers, 1, RosnerMaxOutliers);
        if (n - r0 < 3) r0 = n - 3;

        var remaining = new List<double>(data.ToArray());
        var steps = new List<RosnerStep>(r0);

        for (int i = 1; i <= r0; i++)
        {
            double mean = 0;
            foreach (var v in remaining) mean += v;
            mean /= remaining.Count;
            double ss = 0;
            foreach (var v in remaining) { double d = v - mean; ss += d * d; }
            // Rosner/ESD uses the population sd (denominator n) at each stage —
            // verified against TG Table 7-4 (pyrene: sd 430.1 for the 45 detects).
            double sd = Math.Sqrt(ss / remaining.Count);

            double farthest = remaining[0];
            double bestDist = -1.0;
            int bestIdx = 0;
            for (int j = 0; j < remaining.Count; j++)
            {
                double dist = Math.Abs(remaining[j] - mean);
                if (dist > bestDist) { bestDist = dist; farthest = remaining[j]; bestIdx = j; }
            }
            double r = sd > 0 ? bestDist / sd : 0.0;

            steps.Add(new RosnerStep
            {
                Stage = i,
                Remaining = remaining.Count,
                Mean = mean,
                Sd = sd,
                Value = farthest,
                R = r,
                Critical5 = CriticalValue(remaining.Count, 0.05, i),
                Critical1 = CriticalValue(remaining.Count, 0.01, i),
            });

            remaining.RemoveAt(bestIdx);
        }

        return new RosnerResult { N = n, MaxOutliers = r0, Steps = steps };
    }

    /// <summary>
    /// Critical value lambda for the i-th Rosner/ESD test with nRem observations
    /// remaining: lambda = t(p, nRem-1) * (nRem-1) / sqrt(nRem-2 + t^2) * ...
    /// (generalized ESD: lambda = (n-1) t(p, n-2) / sqrt((n-2 + t^2) n) in terms of
    /// the current sample size n = nRem, with p = 1 - alpha / (2(n+1))).
    /// </summary>
    public static double CriticalValue(int nRem, double alpha, int stage)
    {
        int n = nRem;                       // observations remaining at this stage
        double p = 1.0 - alpha / (2.0 * (n + 1.0));
        double t = StudentT.Quantile(p, n - 2);
        return (n - 1.0) * t / Math.Sqrt((n - 2.0 + t * t) * n);
    }
}
