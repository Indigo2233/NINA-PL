using System;
using System.Collections.Generic;
using OpenCvSharp;

namespace NINA.PL.Guider.TrackingAlgorithms;

/// <summary>
/// Estimates the center of a partially visible solar/lunar disk from limb edges (Hough circles + algebraic fallback).
/// </summary>
public sealed class LimbTracker
{
    /// <summary>
    /// Detects limb geometry and returns an estimate of the full-disk center in pixel coordinates.
    /// </summary>
    public (double centerX, double centerY) DetectLimb(Mat image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Empty())
            throw new ArgumentException("Image is empty.", nameof(image));

        using Mat gray = ToGray(image);
        using Mat blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0, 0);

        using Mat edges = new Mat();
        Cv2.Canny(blurred, edges, 50, 150, apertureSize: 3);

        int minR = Math.Max(8, Math.Min(gray.Cols, gray.Rows) / 80);
        int maxR = Math.Min(gray.Cols, gray.Rows) / 2;

        CircleSegment[] circles = Cv2.HoughCircles(
            edges,
            HoughModes.Gradient,
            dp: 1,
            minDist: Math.Max(minR * 2, 20),
            param1: 120,
            param2: Math.Max(15, minR / 2),
            minRadius: minR,
            maxRadius: maxR);

        if (circles.Length > 0)
        {
            var best = SelectBestCircle(edges, circles);
            return (best.Center.X, best.Center.Y);
        }

        Point2f[] pts = CollectEdgePoints(edges, maxPoints: 4096);
        if (pts.Length < 12)
            throw new InvalidOperationException("LimbTracker: insufficient edge points for circle fit.");

        if (!TryFitCircleLeastSquares(pts, out Point2f c, out float r) || r < minR || float.IsNaN(r))
            throw new InvalidOperationException("LimbTracker: algebraic circle fit failed.");

        return (c.X, c.Y);
    }

    private static CircleSegment SelectBestCircle(Mat edgeMask, CircleSegment[] circles)
    {
        double bestScore = double.NegativeInfinity;
        CircleSegment best = circles[0];

        foreach (var seg in circles)
        {
            double score = ScoreCircle(edgeMask, seg.Center, seg.Radius);
            if (score > bestScore)
            {
                bestScore = score;
                best = seg;
            }
        }

        return best;
    }

    private static double ScoreCircle(Mat edgeMask, Point2f center, float radius)
    {
        if (radius < 1f)
            return double.NegativeInfinity;

        int count = 0;
        int samples = 120;
        double cx = center.X;
        double cy = center.Y;

        for (int i = 0; i < samples; i++)
        {
            double t = 2 * Math.PI * i / samples;
            int x = (int)Math.Round(cx + radius * Math.Cos(t));
            int y = (int)Math.Round(cy + radius * Math.Sin(t));
            if ((uint)x < (uint)edgeMask.Cols && (uint)y < (uint)edgeMask.Rows && edgeMask.At<byte>(y, x) > 0)
                count++;
        }

        return count + radius * 0.05;
    }

    private static Point2f[] CollectEdgePoints(Mat edges, int maxPoints)
    {
        using Mat nz = new Mat();
        Cv2.FindNonZero(edges, nz);
        if (nz.Empty())
            return Array.Empty<Point2f>();

        int n = nz.Rows;
        var list = new List<Point2f>(Math.Min(n, maxPoints));
        var rng = new Random(42);
        if (n <= maxPoints)
        {
            for (int i = 0; i < n; i++)
            {
                Point p = nz.At<Point>(i);
                list.Add(new Point2f(p.X, p.Y));
            }
        }
        else
        {
            var idx = new int[n];
            for (int i = 0; i < n; i++) idx[i] = i;
            for (int i = 0; i < maxPoints; i++)
            {
                int j = rng.Next(i, n);
                (idx[i], idx[j]) = (idx[j], idx[i]);
            }

            for (int i = 0; i < maxPoints; i++)
            {
                Point p = nz.At<Point>(idx[i]);
                list.Add(new Point2f(p.X, p.Y));
            }
        }

        return list.ToArray();
    }

    /// <summary>
    /// Algebraic least squares: x² + y² + d·x + e·y + f = 0 → center (-d/2,-e/2), radius from f.
    /// </summary>
    private static bool TryFitCircleLeastSquares(Point2f[] points, out Point2f center, out float radius)
    {
        center = default;
        radius = 0;

        double a00 = 0, a01 = 0, a02 = 0, a11 = 0, a12 = 0, a22 = 0;
        double r0 = 0, r1 = 0, r2 = 0;

        foreach (var p in points)
        {
            double x = p.X;
            double y = p.Y;
            double s = -(x * x + y * y);

            a00 += x * x;
            a01 += x * y;
            a02 += x;
            a11 += y * y;
            a12 += y;
            a22 += 1;

            r0 += x * s;
            r1 += y * s;
            r2 += s;
        }

        if (!TrySolveSymmetric3(
                a00, a01, a02,
                a11, a12,
                a22,
                r0, r1, r2,
                out double d, out double e, out double f))
            return false;

        double cx = -0.5 * d;
        double cy = -0.5 * e;
        double rSq = cx * cx + cy * cy - f;
        if (rSq <= 0 || double.IsNaN(rSq) || double.IsInfinity(rSq))
            return false;

        center = new Point2f((float)cx, (float)cy);
        radius = (float)Math.Sqrt(rSq);
        return true;
    }

    private static bool TrySolveSymmetric3(
        double a00, double a01, double a02,
        double a11, double a12,
        double a22,
        double b0, double b1, double b2,
        out double x0, out double x1, out double x2)
    {
        x0 = x1 = x2 = 0;

        var r0 = new double[] { a00, a01, a02, b0 };
        var r1 = new double[] { a01, a11, a12, b1 };
        var r2 = new double[] { a02, a12, a22, b2 };

        static void SwapRows(double[] a, double[] b)
        {
            for (int c = 0; c < 4; c++)
                (a[c], b[c]) = (b[c], a[c]);
        }

        int p0 = 0;
        double m0 = Math.Abs(r0[0]);
        if (Math.Abs(r1[0]) > m0) { m0 = Math.Abs(r1[0]); p0 = 1; }
        if (Math.Abs(r2[0]) > m0) p0 = 2;
        if (p0 == 1) SwapRows(r0, r1);
        else if (p0 == 2) SwapRows(r0, r2);

        if (Math.Abs(r0[0]) < 1e-15)
            return false;

        double inv = 1.0 / r0[0];
        for (int c = 0; c < 4; c++) r0[c] *= inv;

        static void Elim0(double[] row, double[] pivot)
        {
            double f = row[0];
            for (int c = 0; c < 4; c++) row[c] -= f * pivot[c];
        }

        Elim0(r1, r0);
        Elim0(r2, r0);

        if (Math.Abs(r1[1]) < Math.Abs(r2[1]))
            SwapRows(r1, r2);

        if (Math.Abs(r1[1]) < 1e-15)
            return false;

        inv = 1.0 / r1[1];
        for (int c = 1; c < 4; c++) r1[c] *= inv;

        {
            double f = r2[1];
            for (int c = 1; c < 4; c++) r2[c] -= f * r1[c];
        }

        if (Math.Abs(r2[2]) < 1e-15)
            return false;

        x2 = r2[3] / r2[2];
        x1 = r1[3] - r1[2] * x2;
        x0 = r0[3] - r0[2] * x2 - r0[1] * x1;
        return true;
    }

    private static Mat ToGray(Mat image)
    {
        if (image.Channels() == 1)
            return image.Clone();

        var gray = new Mat();
        Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        return gray;
    }
}
