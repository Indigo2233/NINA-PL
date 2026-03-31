using System;
using OpenCvSharp;

namespace NINA.PL.Guider.TrackingAlgorithms;

/// <summary>
/// Full-disk planetary tracking via threshold segmentation and centroid from image moments.
/// </summary>
public sealed class DiskCentroidTracker
{
    private const double MinCircularity = 0.65;
    private const double MinAreaRatio = 0.0005;
    private const double MaxAreaRatio = 0.95;

    /// <summary>
    /// Detects the dominant circular disk and returns centroid and equivalent radius.
    /// </summary>
    public (double x, double y, double radius) DetectDisk(Mat image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Empty())
            throw new ArgumentException("Image is empty.", nameof(image));

        using Mat gray = ToGray(image);
        using Mat blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0, 0);

        using Mat binary = new Mat();
        Cv2.Threshold(blurred, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        Cv2.FindContours(
            binary,
            out Point[][] contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        if (contours.Length == 0)
            throw new InvalidOperationException("DiskCentroidTracker: no contours after Otsu threshold.");

        double imgArea = gray.Rows * gray.Cols;
        double minArea = imgArea * MinAreaRatio;
        double maxArea = imgArea * MaxAreaRatio;

        int bestIdx = -1;
        double bestScore = 0;

        for (int i = 0; i < contours.Length; i++)
        {
            var c = contours[i];
            double area = Cv2.ContourArea(c);
            if (area < minArea || area > maxArea)
                continue;

            double peri = Cv2.ArcLength(c, true);
            if (peri < 1e-6)
                continue;

            double circularity = 4.0 * Math.PI * area / (peri * peri);
            if (circularity < MinCircularity)
                continue;

            double score = area * circularity;
            if (score > bestScore)
            {
                bestScore = score;
                bestIdx = i;
            }
        }

        if (bestIdx < 0)
            throw new InvalidOperationException("DiskCentroidTracker: no sufficiently circular contour found.");

        Point[] best = contours[bestIdx];
        Moments m = Cv2.Moments(best);
        if (Math.Abs(m.M00) < 1e-9)
            throw new InvalidOperationException("DiskCentroidTracker: zero contour moment.");

        double cx = m.M10 / m.M00;
        double cy = m.M01 / m.M00;

        Cv2.MinEnclosingCircle(best, out Point2f encCenter, out float encRadius);
        double radius = encRadius > 1 ? encRadius : Math.Sqrt(Cv2.ContourArea(best) / Math.PI);

        return (cx, cy, radius);
    }

    /// <summary>
    /// Measures pixel offset of the disk centroid from a reference position.
    /// </summary>
    public (double dx, double dy) ComputeOffset(Mat image, double refX, double refY)
    {
        var (cx, cy, _) = DetectDisk(image);
        return (cx - refX, cy - refY);
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
