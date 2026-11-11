namespace OpenUCL.Core;

/// <summary>
/// Land's exact method for the lognormal mean (Land 1971, 1975; TG 5.2 section
/// 2.4.3): the H statistic is computed exactly by inverting the distribution of
/// Land's conditional t, obtained by polar integration of
///   f(theta) = exp((nu-1)·ln cos(theta) - (nu/2)·ln nu + (1+nu)·zeta·sin(theta))
/// over theta in (-pi/2, pi/2), zeta being the parameter of the conditional
/// distribution. UCL = exp(meanlog + sdlog²/2 + H·sdlog/√(n−1))
/// (EPA/OSWER 9285.6-10 exhibit 3; TG eq. 2-36).
/// Algorithm structure follows the reference implementation (EnvStats lands.C /
/// plands.t / qlands.t).
/// </summary>
public static class LandExact
{
    /// <summary>Log of the unnormalized polar density (no normalization constant).</summary>
    private static double LogPolarDensity(double theta, double nu, double zeta) =>
        (nu - 1.0) * Math.Log(Math.Cos(theta)) - (nu / 2.0) * Math.Log(nu)
        + (1.0 + nu) * zeta * Math.Sin(theta);

    /// <summary>
    /// Mode of the polar density: root of -(nu-1)·tan(theta) + (1+nu)·zeta·cos(theta)
    /// on (-pi/2, pi/2); used as the log-shift for stable integration.
    /// </summary>
    private static double ThetaMax(double nu, double zeta)
    {
        double lo = -Math.PI / 2.0 + 1e-12, hi = Math.PI / 2.0 - 1e-12;
        for (int i = 0; i < 200; i++)
        {
            double mid = 0.5 * (lo + hi);
            double f = -(nu - 1.0) * Math.Tan(mid) + (1.0 + nu) * zeta * Math.Cos(mid);
            if (f > 0) lo = mid; else hi = mid;
        }
        return 0.5 * (lo + hi);
    }

    private static double ExpSafe(double x) => x < -745.0 ? 0.0 : Math.Exp(x);

    /// <summary>Normalized integral of the density from -pi/2 to theta (composite Simpson).</summary>
    private static double CdfTheta(double theta, double nu, double zeta)
    {
        double thetaMax = ThetaMax(nu, zeta);
        double logMax = LogPolarDensity(thetaMax, nu, zeta);
        double a = -Math.PI / 2.0;
        const int intervals = 1024;
        double h = (theta - a) / intervals;
        if (h <= 0) return 0.0;
        double sum = ExpSafe(LogPolarDensity(a, nu, zeta) - logMax)
            + ExpSafe(LogPolarDensity(theta, nu, zeta) - logMax);
        for (int k = 1; k < intervals; k++)
        {
            double xx = a + k * h;
            double coeff = k % 2 == 1 ? 4.0 : 2.0;
            sum += coeff * ExpSafe(LogPolarDensity(xx, nu, zeta) - logMax);
        }
        double integral = sum * h / 3.0;
        // normalize over (-pi/2, pi/2)
        double hFull = (Math.PI / 2.0 - a) / intervals;
        double norm = 0.0;
        for (int k = 0; k <= intervals; k++)
        {
            double xx = a + k * hFull;
            double coeff = k == 0 || k == intervals ? 1.0 : (k % 2 == 1 ? 4.0 : 2.0);
            norm += coeff * ExpSafe(LogPolarDensity(xx, nu, zeta) - logMax);
        }
        norm *= hFull / 3.0;
        return Math.Min(Math.Max(integral / norm, 0.0), 1.0);
    }

    /// <summary>CDF of Land's conditional t at q: integrates to atan(q/sqrt(nu)).</summary>
    public static double LandTCdf(double q, double nu, double zeta) =>
        CdfTheta(Math.Atan(q / Math.Sqrt(nu)), nu, zeta);

    /// <summary>Quantile of Land's conditional t: P(T <= q) = p (with symmetry mirroring).</summary>
    public static double LandTQuantile(double p, double nu, double zeta)
    {
        if (p is <= 0.0 or >= 1.0)
            throw new ArgumentOutOfRangeException(nameof(p), "p must lie strictly between 0 and 1.");
        if (p > 0.5)
            return -LandTQuantile(1.0 - p, nu, -zeta);
        double lo = -Math.PI / 2.0 + 1e-12, hi = Math.PI / 2.0 - 1e-12;
        for (int i = 0; i < 70; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (CdfTheta(mid, nu, zeta) < p) lo = mid;
            else hi = mid;
        }
        double theta = 0.5 * (lo + hi);
        return Math.Sqrt(nu) * Math.Tan(theta);
    }

    /// <summary>
    /// Land's H critical factor (the value tabulated by Land 1975 / Gilbert
    /// Tables A10-A12): solves T(m) = qlands.t(alpha, nu, zeta(m)) where
    ///   T(m)   = sqrt(nu+1) * ((-s^2)/2 - m) / s
    ///   zeta(m) = -s * sqrt(nu + T(m)^2) / (2 * sqrt(nu+1))
    /// and returns m * sqrt(nu) / s = H. Validated against: EPA/OSWER 9285.6-10
    /// exhibit 4 (n=31, s=0.8995 -> H=2.31, UCL=14.4) and TG Table 2-3/2-4
    /// (silver: H ~ 2.135 / 3.115 back-calculated from UCLs 18.54 / 11.11).
    /// </summary>
    public static double LandCriticalH(int n, double sdLog, double cc)
    {
        double nu = n - 1.0;
        double alpha = 1.0 - cc;
        double S = sdLog;
        if (S <= 0) throw new ArgumentOutOfRangeException(nameof(sdLog));

        // Residual g(m) = T(m) - quantile(alpha, zeta(m)): g(0) > 0 (T(0) sits
        // right of the alpha-quantile) and g(m) -> -inf as m -> +inf (T decays
        // linearly, the quantile only like sqrt(m)), so the root m* > 0 and
        // H = m*·sqrt(nu)/s > 0. Bracket [0, hi) with geometric expansion.
        double Residual(double m)
        {
            double t = TMid(m, S, nu);
            double zeta = ZetaMid(m, S, nu);
            return t - LandTQuantile(alpha, nu, zeta);
        }

        double lo = 0.0, hi = 4.0 * S;
        while (Residual(hi) >= 0.0 && hi < 1e6 * S) hi *= 2.0;
        if (Residual(lo) <= 0.0 || Residual(hi) >= 0.0)
            throw new InvalidOperationException("Land H: failed to bracket the critical value.");
        for (int i = 0; i < 90; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (Residual(mid) > 0) lo = mid; else hi = mid;
        }
        double mStar = 0.5 * (lo + hi);
        return mStar * Math.Sqrt(nu) / S;   // = H (the tabulated factor)
    }

    private static double TMid(double m, double S, double nu) =>
        Math.Sqrt(nu + 1.0) * ((-S * S / 2.0) - m) / S;

    private static double ZetaMid(double m, double S, double nu)
    {
        double t = TMid(m, S, nu);
        return -S * Math.Sqrt(nu + t * t) / (2.0 * Math.Sqrt(nu + 1.0));
    }

    /// <summary>
    /// Land's exact one-sided (1-alpha) UCL for the lognormal mean:
    /// exp(meanlog + sdlog²/2 + H·sdlog/√(n−1)) (TG eq. 2-36; EPA/OSWER 9285.6-10).
    /// </summary>
    public static double LandUcl(double meanLog, double sdLog, int n, double cc = 0.95)
    {
        if (n < 4) throw new ArgumentException("Land's method requires at least 4 observations.", nameof(n));
        if (cc is < 0.5 or >= 1.0) throw new ArgumentOutOfRangeException(nameof(cc));
        double h = LandCriticalH(n, sdLog, cc);
        return Math.Exp(meanLog + sdLog * sdLog / 2.0 + h * sdLog / Math.Sqrt(n - 1.0));
    }
}
