namespace OpenUCL.Core;

/// <summary>
/// Maximum-likelihood fit of a two-parameter gamma distribution G(k, θ), with the
/// bias-corrected estimates used by ProUCL ("k star", "theta star").
/// Reference: ProUCL Technical Guide 5.2 §2.3.3, eq. 2-18..2-30
/// (Thom 1968 start, Newton-Raphson with digamma/trigamma per Choi and Wette 1969,
/// bias correction k* = (n-3)k/n + 2/(3n)).
/// </summary>
public sealed record GammaMLE(
    int N,
    double Mean,
    double M,          // M = ln(mean) - mean(ln x)
    double KHat,       // MLE of the shape parameter
    double ThetaHat,   // MLE of the scale parameter (mean / khat)
    double KStar,      // bias-corrected shape
    double ThetaStar,  // bias-corrected scale
    int Iterations);

public static class GammaDistribution
{
    /// <summary>Fits k and θ by MLE. Requires at least 3 strictly positive values.</summary>
    public static GammaMLE FitMLE(ReadOnlySpan<double> data)
    {
        int n = data.Length;
        if (n < 3) throw new ArgumentException("Gamma fit requires at least 3 observations.", nameof(data));

        double sum = 0, sumLog = 0;
        foreach (double v in data)
        {
            if (v <= 0 || double.IsNaN(v))
                throw new ArgumentException("Gamma fit requires strictly positive observations.", nameof(data));
            sum += v;
            sumLog += Math.Log(v);
        }
        double mean = sum / n;
        double m = Math.Log(mean) - sumLog / n;
        if (m <= 0)
            throw new ArgumentException("Gamma fit requires ln(mean) > mean(ln x); data do not allow a gamma shape fit.", nameof(data));

        // Thom (1968) starting value (TG eq. 2-27).
        double k = (1.0 + Math.Sqrt(1.0 + 4.0 * m / 3.0)) / (4.0 * m);

        // Newton-Raphson (TG eq. 2-22); convergence typically in <10 iterations.
        int iterations = 0;
        for (int i = 0; i < 200; i++)
        {
            iterations++;
            double num = Math.Log(k) - SpecialFunctions.Digamma(k) - m;
            double den = 1.0 / k - SpecialFunctions.Trigamma(k);
            double next = k - num / den;
            if (double.IsNaN(next) || next <= 0) break;
            bool converged = Math.Abs(next - k) < 1e-12 * Math.Max(1.0, k);
            k = next;
            if (converged) break;
        }

        double kStar = (n - 3.0) * k / n + 2.0 / (3.0 * n);   // TG eq. 2-29
        return new GammaMLE(n, mean, m, k, mean / k, kStar, mean / kStar, iterations);
    }

    /// <summary>Gamma CDF F(x; k, θ) = P(k, x/θ).</summary>
    public static double Cdf(double x, double k, double theta)
    {
        if (k <= 0 || theta <= 0) throw new ArgumentOutOfRangeException(nameof(theta), "k and theta must be positive");
        if (x <= 0) return 0.0;
        return SpecialFunctions.GammaP(k, x / theta);
    }
}

public enum GammaGofTest { AndersonDarling, KolmogorovSmirnov }

/// <summary>
/// Gamma goodness-of-fit: K-S statistic D and Anderson-Darling A² computed on
/// F(x_i; k̂, θ̂) (the MLE estimates — the same choice ProUCL uses for the GOF,
/// Technical Guide §2.2.2.2 eq. 2-5/2-6 and the Appendix A note).
/// </summary>
public sealed record GammaGofResult(int N, double KHat, double ThetaHat, double KS_D, double AD_A2)
{
    /// <summary>Critical value at alpha (0.10, 0.05, 0.01); rejects when the statistic exceeds it.</summary>
    public double CriticalValue(GammaGofTest test, double alpha) =>
        GammaGofCriticalValues.Lookup(test, alpha, N, KHat);
}

public static class GammaGof
{
    public static GammaGofResult Compute(ReadOnlySpan<double> data)
    {
        int n = data.Length;
        if (n < 3) throw new ArgumentException("Gamma GOF requires at least 3 observations.", nameof(data));

        var fit = GammaDistribution.FitMLE(data);

        double[] x = data.ToArray();
        Array.Sort(x);
        var z = new double[n];
        for (int i = 0; i < n; i++) z[i] = GammaDistribution.Cdf(x[i], fit.KHat, fit.ThetaHat);
        Array.Sort(z);

        double dPlus = 0, dMinus = 0, adSum = 0;
        for (int i = 0; i < n; i++)
        {
            dPlus = Math.Max(dPlus, (i + 1.0) / n - z[i]);
            dMinus = Math.Max(dMinus, z[i] - i / (double)n);
            int mirror = n - i; // z(n+1-i) in 1-based terms
            adSum += (2 * i + 1) * (Math.Log(z[i]) + Math.Log(1.0 - z[mirror - 1]));
        }
        double a2 = Math.Max(-n - adSum / n, 0.0);

        return new GammaGofResult(n, fit.KHat, fit.ThetaHat, Math.Max(dPlus, dMinus), a2);
    }
}
