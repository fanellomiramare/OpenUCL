namespace OpenUCL.Core;

/// <summary>
/// Standard normal distribution functions.
/// Quantile: Wichura's algorithm AS 241 (double-precision rational approximations,
/// central + two tail regions). CDF: erfc-based with a power series for the inner
/// region and a Lentz continued fraction for the tail.
/// </summary>
public static class NormalDistribution
{
    // ---- AS 241 central region coefficients (|q| <= 0.425, r = 0.180625 - q^2) ----
    private const double A0 = 3.3871328727963666080;
    private const double A1 = 1.3314166789178437745e2;
    private const double A2 = 1.9715909503065514427e3;
    private const double A3 = 1.3731693765509461125e4;
    private const double A4 = 4.5921953931549871457e4;
    private const double A5 = 6.7265770927008700853e4;
    private const double A6 = 3.3430575583588128105e4;
    private const double A7 = 2.5090809287301226727e3;
    private const double B1 = 4.2313330701600911252e1;
    private const double B2 = 6.8718700749205790830e2;
    private const double B3 = 5.3941960214247511077e3;
    private const double B4 = 2.1213794301586595867e4;
    private const double B5 = 3.9307895800092710610e4;
    private const double B6 = 2.8729085735721942674e4;
    private const double B7 = 5.2264952788528545610e3;

    // ---- AS 241 tail region 1 (sqrt(-log(p)) <= 5, shifted by -1.6) ----
    private const double C0 = 1.42343711074968357734;
    private const double C1 = 4.63033784615654529590;
    private const double C2 = 5.76949722146069140550;
    private const double C3 = 3.64784832476320460504;
    private const double C4 = 1.27045825245236838258;
    private const double C5 = 2.41780725177450611770e-1;
    private const double C6 = 2.27238449892691845833e-2;
    private const double C7 = 7.74545014278341407640e-4;
    private const double D1 = 2.05319162663775882187;
    private const double D2 = 1.67638483018380384940;
    private const double D3 = 6.89767334985100004550e-1;
    private const double D4 = 1.48103976427480074590e-1;
    private const double D5 = 1.51986665636164571966e-2;
    private const double D6 = 5.47593808499534494600e-4;
    private const double D7 = 1.05075007164441684324e-9;

    // ---- AS 241 tail region 2 (sqrt(-log(p)) <= 27, shifted by -5) ----
    private const double E0 = 6.65790464350110377720;
    private const double E1 = 5.46378491116411436990;
    private const double E2 = 1.78482653991729133580;
    private const double E3 = 2.96560571828504891230e-1;
    private const double E4 = 2.65321895265761230930e-2;
    private const double E5 = 1.24266094738807843860e-3;
    private const double E6 = 2.71155556874348757815e-5;
    private const double E7 = 2.01033439929228813265e-7;
    private const double F1 = 5.99832206555887937690e-1;
    private const double F2 = 1.36929880922735805310e-1;
    private const double F3 = 1.48753612908506148525e-2;
    private const double F4 = 7.86869131145613259100e-4;
    private const double F5 = 1.84631831751005468180e-5;
    private const double F6 = 1.42151175831644588870e-7;
    private const double F7 = 2.04426310338993978564e-15;

    /// <summary>Inverse standard normal CDF (probit), p in (0,1).</summary>
    public static double Quantile(double p)
    {
        if (p is <= 0.0 or >= 1.0)
            throw new ArgumentOutOfRangeException(nameof(p), "p must lie strictly between 0 and 1.");

        double q = p - 0.5;
        if (Math.Abs(q) <= 0.425)
        {
            double r = 0.180625 - q * q;
            double num = (((((((A7 * r + A6) * r + A5) * r + A4) * r + A3) * r + A2) * r + A1) * r + A0) * q;
            double den = ((((((B7 * r + B6) * r + B5) * r + B4) * r + B3) * r + B2) * r + B1) * r + 1.0;
            return num / den;
        }

        double tail = q < 0 ? p : 1.0 - p;
        double r2 = Math.Sqrt(-Math.Log(tail));
        double val;
        if (r2 <= 5.0)
        {
            r2 -= 1.6;
            double num = (((((((C7 * r2 + C6) * r2 + C5) * r2 + C4) * r2 + C3) * r2 + C2) * r2 + C1) * r2 + C0);
            double den = ((((((D7 * r2 + D6) * r2 + D5) * r2 + D4) * r2 + D3) * r2 + D2) * r2 + D1) * r2 + 1.0;
            val = num / den;
        }
        else if (r2 <= 27.0)
        {
            r2 -= 5.0;
            double num = (((((((E7 * r2 + E6) * r2 + E5) * r2 + E4) * r2 + E3) * r2 + E2) * r2 + E1) * r2 + E0);
            double den = ((((((F7 * r2 + F6) * r2 + F5) * r2 + F4) * r2 + F3) * r2 + F2) * r2 + F1) * r2 + 1.0;
            val = num / den;
        }
        else
        {
            // Beyond AS 241 design range: asymptotic first-order approximation.
            val = Math.Sqrt(2.0 * -Math.Log(tail));
        }
        return q < 0 ? -val : val;
    }

    /// <summary>Standard normal CDF Φ(z).</summary>
    public static double Cdf(double z) => 0.5 * Erfc(-z / Math.Sqrt(2.0));

    private const double ErfcSwitch = 1.224744871391589; // sqrt(a+1) with a = 1/2

    /// <summary>Complementary error function, x any real (relative error ~1e-13).</summary>
    /// erfc(x) = Q(1/2, x^2): upper incomplete gamma ratio with shape 1/2.
    internal static double Erfc(double x)
    {
        if (x < 0) return 2.0 - Erfc(-x);
        double a = 0.5, xx = x * x;
        return xx < a + 1.0 ? 1.0 - GammaPSeries(a, xx) : GammaQContinuedFraction(a, xx);
    }

    // Regularized lower incomplete gamma P(a,x) by series (standard gser algorithm).
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

    // Regularized upper incomplete gamma Q(a,x) by modified Lentz continued fraction (gcf).
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

    // Lanczos approximation of ln(Gamma(x)), x > 0 (g=7, n=9 coefficients).
    internal static double LogGamma(double x)
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
}
