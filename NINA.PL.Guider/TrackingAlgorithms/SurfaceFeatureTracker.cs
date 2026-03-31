using System;
using OpenCvSharp;

namespace NINA.PL.Guider.TrackingAlgorithms;

/// <summary>
/// Normalized cross-correlation template matching for surface detail tracking.
/// </summary>
public sealed class SurfaceFeatureTracker
{
    private Mat? _template;
    private double _refCenterX;
    private double _refCenterY;

    /// <summary>
    /// Extracts and stores the reference template; reference position is the ROI center in full-image coordinates.
    /// </summary>
    public void SetReferenceTemplate(Mat image, int roiX, int roiY, int roiWidth, int roiHeight)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (roiWidth <= 0 || roiHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(roiWidth), "ROI width and height must be positive.");

        int x1 = Math.Clamp(roiX, 0, Math.Max(0, image.Cols - 1));
        int y1 = Math.Clamp(roiY, 0, Math.Max(0, image.Rows - 1));
        int x2 = Math.Clamp(roiX + roiWidth, 0, image.Cols);
        int y2 = Math.Clamp(roiY + roiHeight, 0, image.Rows);
        int w = x2 - x1;
        int h = y2 - y1;
        if (w < 8 || h < 8)
            throw new ArgumentException("ROI is too small after clipping to image bounds.", nameof(roiWidth));

        using Mat grayFull = ToGray(image);
        var roiRect = new Rect(x1, y1, w, h);
        using Mat roi = new Mat(grayFull, roiRect);

        _template?.Dispose();
        _template = roi.Clone();
        _refCenterX = x1 + w * 0.5;
        _refCenterY = y1 + h * 0.5;
    }

    /// <summary>
    /// Returns pixel offset of the matched template center from the stored reference center and match confidence in [0,1].
    /// </summary>
    public (double dx, double dy, double confidence) Track(Mat image)
    {
        if (_template is null || _template.Empty())
            throw new InvalidOperationException("SetReferenceTemplate must be called before Track.");

        ArgumentNullException.ThrowIfNull(image);
        if (image.Empty())
            throw new ArgumentException("Image is empty.", nameof(image));

        using Mat gray = ToGray(image);
        if (gray.Rows < _template.Rows || gray.Cols < _template.Cols)
            throw new InvalidOperationException("Image is smaller than the reference template.");

        using Mat result = new Mat();
        Cv2.MatchTemplate(gray, _template, result, TemplateMatchModes.CCoeffNormed);

        Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out Point maxLoc);

        double tplCx = maxLoc.X + _template.Cols * 0.5;
        double tplCy = maxLoc.Y + _template.Rows * 0.5;
        double dx = tplCx - _refCenterX;
        double dy = tplCy - _refCenterY;

        double confidence = Math.Clamp(maxVal, 0.0, 1.0);
        return (dx, dy, confidence);
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
