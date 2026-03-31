using System;
using System.Collections.Generic;
using System.Linq;
using NINA.PL.Core;

namespace NINA.PL.AutoFocus;

/// <summary>
/// Fits focus-vs-position curves (parabolic V-curve and reciprocal / hyperbolic-style models) with plain linear algebra.
/// </summary>
public static class FocusCurveFitter
{
    private const double Epsilon = 1e-9;
    private const double MinCurvature = 1e-12;

    /// <summary>
    /// Least-squares fit y ≈ ax² + bx + c. For a downward-opening parabola (a &lt; 0), the vertex is the maximum metric.
    /// </summary>
    public static (int bestPosition, double bestMetric) FitParabola(List<FocusPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count < 3)
        {
            return MaxSample(points);
        }

        if (!TrySolveQuadraticFit(points, static p => p.MetricValue, out double a, out double b, out double c))
        {
            return MaxSample(points);
        }

        // Focus metrics increase toward best focus → expect concave-down parabola (a < 0) with maximum at vertex.
        if (a >= -MinCurvature)
        {
            return MaxSample(points);
        }

        double xv = -b / (2.0 * a);
        int minX = points.Min(p => p.Position);
        int maxX = points.Max(p => p.Position);
        int best = ClampInt((int)Math.Round(xv), minX, maxX);
        double bestMetric = a * best * best + b * best + c;
        return (best, bestMetric);
    }

    /// <summary>
    /// Fits w = 1 / (y + ε) as a quadratic in position; minimizing w corresponds to maximizing the metric y.
    /// Returns the vertex position of the w-parabola (minimum w), which is treated as the best focus position.
    /// </summary>
    public static (int bestPosition, double bestMetric) FitHyperbola(List<FocusPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count < 3)
        {
            return MaxSample(points);
        }

        if (!TrySolveQuadraticFit(
                points,
                static p => 1.0 / (p.MetricValue + Epsilon),
                out double a,
                out double b,
                out double c))
        {
            return MaxSample(points);
        }

        // Minimum of w → maximum of y; need a > 0 for a finite minimum.
        if (a <= MinCurvature)
        {
            return MaxSample(points);
        }

        double xv = -b / (2.0 * a);
        int minX = points.Min(p => p.Position);
        int maxX = points.Max(p => p.Position);
        int best = ClampInt((int)Math.Round(xv), minX, maxX);

        double yAtBest = InterpolateMetricAtPosition(points, best);
        return (best, yAtBest);
    }

    /// <summary>
    /// Prefers a parabolic fit of the raw metric; falls back to the sampled point with the highest metric when the fit is unusable.
    /// </summary>
    public static int FindBestPosition(List<FocusPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
        {
            throw new ArgumentException("At least one focus sample is required.", nameof(points));
        }

        (int bestPosition, _) = FitParabola(points);
        return bestPosition;
    }

    private static (int bestPosition, double bestMetric) MaxSample(List<FocusPoint> points)
    {
        var best = points.MaxBy(p => p.MetricValue)!;
        return (best.Position, best.MetricValue);
    }

    private static double InterpolateMetricAtPosition(List<FocusPoint> points, int position)
    {
        FocusPoint? exact = points.Find(p => p.Position == position);
        if (exact is not null)
        {
            return exact.MetricValue;
        }

        var ordered = points.OrderBy(p => p.Position).ToList();
        for (int i = 0; i < ordered.Count - 1; i++)
        {
            int x0 = ordered[i].Position;
            int x1 = ordered[i + 1].Position;
            if (position > x0 && position < x1)
            {
                double t = (position - x0) / (double)(x1 - x0);
                return ordered[i].MetricValue * (1 - t) + ordered[i + 1].MetricValue * t;
            }
        }

        return ordered.OrderBy(p => Math.Abs(p.Position - position)).First().MetricValue;
    }

    private static bool TrySolveQuadraticFit(
        List<FocusPoint> points,
        Func<FocusPoint, double> ySelector,
        out double a,
        out double b,
        out double c)
    {
        double s4 = 0, s3 = 0, s2 = 0, s1 = 0;
        double t2 = 0, t1 = 0, t0 = 0;
        int n = 0;

        foreach (FocusPoint p in points)
        {
            double x = p.Position;
            double y = ySelector(p);
            double x2 = x * x;
            double x3 = x2 * x;
            double x4 = x2 * x2;

            s4 += x4;
            s3 += x3;
            s2 += x2;
            s1 += x;
            t2 += y * x2;
            t1 += y * x;
            t0 += y;
            n++;
        }

        // Normal equations for y ≈ a*x² + b*x + c
        if (!TrySolve3x3(s4, s3, s2, t2, s3, s2, s1, t1, s2, s1, n, t0, out a, out b, out c))
        {
            return false;
        }

        if (double.IsNaN(a) || double.IsNaN(b) || double.IsNaN(c) ||
            double.IsInfinity(a) || double.IsInfinity(b) || double.IsInfinity(c))
        {
            return false;
        }

        return true;
    }

    /// <summary>Solves a 3×3 linear system via Gaussian elimination with partial pivoting.</summary>
    private static bool TrySolve3x3(
        double m00,
        double m01,
        double m02,
        double r0,
        double m10,
        double m11,
        double m12,
        double r1,
        double m20,
        double m21,
        double m22,
        double r2,
        out double x0,
        out double x1,
        out double x2)
    {
        Span<double> m = stackalloc double[12]
        {
            m00, m01, m02, r0,
            m10, m11, m12, r1,
            m20, m21, m22, r2,
        };

        const int rows = 3;
        const int cols = 4;

        for (int col = 0; col < 3; col++)
        {
            int pivot = col;
            double maxAbs = Math.Abs(m[col * cols + col]);
            for (int row = col + 1; row < rows; row++)
            {
                double v = Math.Abs(m[row * cols + col]);
                if (v > maxAbs)
                {
                    maxAbs = v;
                    pivot = row;
                }
            }

            if (maxAbs < 1e-15)
            {
                x0 = x1 = x2 = 0;
                return false;
            }

            if (pivot != col)
            {
                for (int j = col; j < cols; j++)
                {
                    int ia = pivot * cols + j;
                    int ib = col * cols + j;
                    (m[ia], m[ib]) = (m[ib], m[ia]);
                }
            }

            double div = m[col * cols + col];
            for (int j = col; j < cols; j++)
            {
                m[col * cols + j] /= div;
            }

            for (int row = 0; row < rows; row++)
            {
                if (row == col)
                {
                    continue;
                }

                double factor = m[row * cols + col];
                if (Math.Abs(factor) < 1e-18)
                {
                    continue;
                }

                for (int j = col; j < cols; j++)
                {
                    m[row * cols + j] -= factor * m[col * cols + j];
                }
            }
        }

        x0 = m[3];
        x1 = m[7];
        x2 = m[11];
        return true;
    }

    private static int ClampInt(int value, int min, int max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }
}
