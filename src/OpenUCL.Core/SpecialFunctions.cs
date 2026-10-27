namespace OpenUCL.Core;

/// <summary>
/// Special functions used by the distribution machinery.
/// References: Lanczos log-gamma; regularized incomplete gamma by series (gser) and
/// modified Lentz continued fraction (gcf) — ProUCL Technical Guide 5.2 §2.2.2.2 cites
/// Algorithm AS 239 (Shea 1988) / Numerical Recipes for the incomplete gamma;
/// digamma/trigamma asymptotics and recurrences per Choi and Wette (1969), used by
/// ProUCL for the gamma MLE (Technical Guide eq. 2-23..2-26).
/// </summary>
public static class SpecialFunctions
{
    /// <summary>ln Γ(x), x &gt; 0 (Lanczos, g=7).</summary>
    public static double LogGamma(double x)
    {
        double[] cof =
        {
            76.18009172947146, -86.50532032941677, 24.01409824083091,
            -1.231739572450155, 0.1208650973866179e-2, -0.5395239384953e-5,
        };
        double y = x;
        double tmp = x + 5.5;
        tmp -= (x + 0.5) * Math.Log(tmp);
        double ser = 1.000000000190015;
        for (int j = 0; j < 6; j++) ser += cof[j] / ++y;
        return -tmp + Math.Log(2.5066282746310005 * ser / x);
    }

    /// <summary>Regularized lower incomplete gamma P(a, x), a &gt; 0, x ≥ 0.</summary>
    public static double GammaP(double a, double x)
    {
        if (x < 0) throw new ArgumentOutOfRangeException(nameof(x));
        if (x == 0) return 0.0;
        return x < a + 1.0 ? GammaPSeries(a, x) : 1.0 - GammaQContinuedFraction(a, x);
    }

    /// <summary>Regularized upper incomplete gamma Q(a, x) = 1 − P(a, x).</summary>
    public static double GammaQ(double a, double x) => 1.0 - GammaP(a, x);

    private static double GammaPSeries(double a, double x)
    {
        double ap = a;
        double sum = 1.0 / a;
        double del = sum;
        for (int n = 1; n < 1000; n++)
        {
            ap += 1.0;
            del *= x / ap;
            sum += del;
            if (Math.Abs(del) < Math.Abs(sum) * 1e-16) break;
        }
        return sum * Math.Exp(-x + a * Math.Log(x) - LogGamma(a));
    }

    private static double GammaQContinuedFraction(double a, double x)
    {
        const double fpmin = 1e-300;
        double b = x + 1.0 - a;
        double c = 1.0 / fpmin;
        double d = 1.0 / b;
        double h = d;
        for (int i = 1; i < 1000; i++)
        {
            double an = -i * (i - a);
            b += 2.0;
            d = an * d + b;
            if (Math.Abs(d) < fpmin) d = fpmin;
            c = b + an / c;
            if (Math.Abs(c) < fpmin) c = fpmin;
            d = 1.0 / d;
            double del = d * c;
            h *= del;
            if (Math.Abs(del - 1.0) < 1e-16) break;
        }
        return Math.Exp(-x + a * Math.Log(x) - LogGamma(a)) * h;
    }

    /// <summary>Digamma Ψ(k) (Choi and Wette 1969 approximations; TG eq. 2-23/2-25).</summary>
    public static double Digamma(double k)
    {
        if (k <= 0) throw new ArgumentOutOfRangeException(nameof(k));
        if (k < 8.0)
        {
            // Recurrence down from the asymptotic region: Psi(k) = Psi(k+m) - sum 1/(k+j)
            double m = Math.Ceiling(8.0 - k);
            double psi = Digamma(k + m);
            for (int j = (int)m - 1; j >= 0; j--) psi -= 1.0 / (k + j);
            return psi;
        }
        return Math.Log(k)
             - (1.0 + (1.0 - (1.0 / 10.0 - 1.0 / (k * k)) / (k * k)) / (6.0 * k)) / (2.0 * k);
    }

    /// <summary>Trigamma Ψ′(k) (Choi and Wette 1969; TG eq. 2-24/2-26).</summary>
    public static double Trigamma(double k)
    {
        if (k <= 0) throw new ArgumentOutOfRangeException(nameof(k));
        if (k < 8.0)
        {
            double m = Math.Ceiling(8.0 - k);
            double tri = Trigamma(k + m);
            for (int j = (int)m - 1; j >= 0; j--) tri += 1.0 / ((k + j) * (k + j));
            return tri;
        }
        return (1.0
              + (1.0 + (1.0 - (1.0 / 5.0 - 1.0 / (7.0 * k * k)) / (k * k)) / (3.0 * k)) / (2.0 * k)
               ) / k;
    }

    /// <summary>Complementary error function, x any real (relative error ~1e-13). erfc(x) = Q(1/2, x²).</summary>
    internal static double Erfc(double x)
    {
        if (x < 0) return 2.0 - Erfc(-x);
        return GammaQ(0.5, x * x);
    }

    /// <summary>
    /// Regularized incomplete beta function I_x(a, b), a,b &gt; 0, 0 &lt;= x &lt;= 1
    /// (power series near the edges, symmetric continued fraction inside).
    /// </summary>
    public static double BetaI(double x, double a, double b)
    {
        if (x <= 0) return 0.0;
        if (x >= 1) return 1.0;
        double bt = Math.Exp(LogGamma(a + b) - LogGamma(a) - LogGamma(b) + a * Math.Log(x) + b * Math.Log(1.0 - x));
        if (x < (a + 1.0) / (a + b + 2.0))
            return bt * BetaContinuedFraction(x, a, b) / a;
        return 1.0 - bt * BetaContinuedFraction(1.0 - x, b, a) / b;
    }

    private static double BetaContinuedFraction(double x, double a, double b)
    {
        const int maxIterations = 300;
        const double eps = 3e-16, fpmin = 1e-300;
        double qab = a + b, qap = a + 1.0, qam = a - 1.0;
        double c = 1.0, d = 1.0 - qab * x / qap;
        if (Math.Abs(d) < fpmin) d = fpmin;
        d = 1.0 / d;
        double h = d;
        for (int i = 1; i <= maxIterations; i++)
        {
            int m2 = 2 * i;
            double aa = i * (b - i) * x / ((qam + m2) * (a + m2));
            d = 1.0 + aa * d;
            if (Math.Abs(d) < fpmin) d = fpmin;
            c = 1.0 + aa / c;
            if (Math.Abs(c) < fpmin) c = fpmin;
            d = 1.0 / d;
            h *= d * c;
            aa = -(a + i) * (qab + i) * x / ((a + m2) * (qap + m2));
            d = 1.0 + aa * d;
            if (Math.Abs(d) < fpmin) d = fpmin;
            c = 1.0 + aa / c;
            if (Math.Abs(c) < fpmin) c = fpmin;
            d = 1.0 / d;
            double del = d * c;
            h *= del;
            if (Math.Abs(del - 1.0) < eps) break;
        }
        return h;
    }
}
