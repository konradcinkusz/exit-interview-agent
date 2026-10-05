namespace ExitInterviewAgent.Signals;

/// <summary>
/// Student's t distribution, written from scratch (no dependency to vet, license or keep patched): the regularised incomplete beta
/// function by the modified Lentz continued fraction, and the quantile by bisection on the CDF. Verified in
/// <c>StudentTTests</c> against the textbook table of two-sided 95% critical values.
/// </summary>
internal static class StudentT
{
    /// <summary>The 97.5th percentile (the two-sided 95% critical value) for <paramref name="degreesOfFreedom"/> (not necessarily an integer).</summary>
    public static double Quantile975(double degreesOfFreedom) => Quantile(0.975, degreesOfFreedom);

    public static double Quantile(double p, double degreesOfFreedom)
    {
        if (p is <= 0 or >= 1 || degreesOfFreedom <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(p), "p must be in (0, 1) and the degrees of freedom positive.");
        }
        if (p == 0.5)
        {
            return 0;
        }
        if (p < 0.5)
        {
            return -Quantile(1 - p, degreesOfFreedom);
        }
        double lo = 0, hi = 1;
        while (Cdf(hi, degreesOfFreedom) < p)
        {
            hi *= 2;
            if (hi > 1e12)
            {
                break;
            }
        }
        for (var i = 0; i < 200; i++)
        {
            var mid = (lo + hi) / 2;
            if (Cdf(mid, degreesOfFreedom) < p)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }
        return (lo + hi) / 2;
    }

    public static double Cdf(double t, double v)
    {
        var x = v / (v + t * t);
        var tail = 0.5 * RegularizedIncompleteBeta(x, v / 2, 0.5);
        return t >= 0 ? 1 - tail : tail;
    }

    /// <summary>I_x(a, b), by the continued fraction (Numerical Recipes 6.4), using the symmetry relation where it converges faster.</summary>
    internal static double RegularizedIncompleteBeta(double x, double a, double b)
    {
        if (x <= 0)
        {
            return 0;
        }
        if (x >= 1)
        {
            return 1;
        }
        var front = Math.Exp(LogGamma(a + b) - LogGamma(a) - LogGamma(b) + a * Math.Log(x) + b * Math.Log(1 - x));
        return x < (a + 1) / (a + b + 2)
            ? front * ContinuedFraction(x, a, b) / a
            : 1 - front * ContinuedFraction(1 - x, b, a) / b;
    }

    private static double ContinuedFraction(double x, double a, double b)
    {
        const double tiny = 1e-300;
        const double epsilon = 1e-15;
        var qab = a + b;
        var qap = a + 1;
        var qam = a - 1;
        var c = 1.0;
        var d = 1 - qab * x / qap;
        if (Math.Abs(d) < tiny)
        {
            d = tiny;
        }
        d = 1 / d;
        var h = d;
        for (var m = 1; m <= 500; m++)
        {
            var m2 = 2 * m;
            var aa = m * (b - m) * x / ((qam + m2) * (a + m2));
            d = 1 + aa * d;
            if (Math.Abs(d) < tiny)
            {
                d = tiny;
            }
            c = 1 + aa / c;
            if (Math.Abs(c) < tiny)
            {
                c = tiny;
            }
            d = 1 / d;
            h *= d * c;
            aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
            d = 1 + aa * d;
            if (Math.Abs(d) < tiny)
            {
                d = tiny;
            }
            c = 1 + aa / c;
            if (Math.Abs(c) < tiny)
            {
                c = tiny;
            }
            d = 1 / d;
            var delta = d * c;
            h *= delta;
            if (Math.Abs(delta - 1) < epsilon)
            {
                break;
            }
        }
        return h;
    }

    /// <summary>ln Gamma(x) by the Lanczos approximation (g = 7, n = 9).</summary>
    internal static double LogGamma(double x)
    {
        double[] g =
        [
            0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313,
            -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7,
        ];
        if (x < 0.5)
        {
            return Math.Log(Math.PI / Math.Abs(Math.Sin(Math.PI * x))) - LogGamma(1 - x);
        }
        x -= 1;
        var sum = g[0];
        var t = x + 7.5;
        for (var i = 1; i < 9; i++)
        {
            sum += g[i] / (x + i);
        }
        return 0.5 * Math.Log(2 * Math.PI) + (x + 0.5) * Math.Log(t) - t + Math.Log(sum);
    }
}
