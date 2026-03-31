using OpenCvSharp;

namespace NINA.PL.LiveStack;

/// <summary>
/// Sub-pixel frame alignment using phase correlation (translation-only).
/// </summary>
public static class AlignmentEngine
{
    /// <summary>
    /// Converts <paramref name="src"/> to single-channel <see cref="MatType.CV_32FC1"/> for phase correlation.
    /// </summary>
    private static Mat ToFloatGray(Mat src)
    {
        ArgumentNullException.ThrowIfNull(src);
        if (src.Empty())
            throw new ArgumentException("Input image is empty.", nameof(src));

        using Mat gray = new Mat();
        if (src.Channels() == 1)
            src.CopyTo(gray);
        else if (src.Channels() == 3)
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
        else if (src.Channels() == 4)
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGRA2GRAY);
        else
            throw new NotSupportedException($"Unsupported channel count: {src.Channels()}.");

        var f32 = new Mat();
        gray.ConvertTo(f32, MatType.CV_32FC1);
        return f32;
    }

    /// <summary>
    /// Estimates translational shift between <paramref name="reference"/> and <paramref name="target"/>
    /// using <see cref="Cv2.PhaseCorrelate"/> on float grayscale images.
    /// </summary>
    /// <returns>
    /// <paramref name="dx"/>, <paramref name="dy"/> such that
    /// <see cref="AlignFrame(OpenCvSharp.Mat,double,double)"/> on <paramref name="target"/> aligns it to <paramref name="reference"/>.
    /// </returns>
    public static (double dx, double dy) ComputeShift(Mat reference, Mat target)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(target);
        if (reference.Size() != target.Size())
            throw new ArgumentException("Reference and target must have the same dimensions.", nameof(target));

        using Mat refF = ToFloatGray(reference);
        using Mat tgtF = ToFloatGray(target);
        using Mat window = new Mat();
        Cv2.CreateHanningWindow(window, refF.Size(), MatType.CV_32FC1);
        // Shift to apply to the first image (reference) to coincide with the second (target); AlignFrame uses (-dx,-dy) warp.
        Point2d shift = Cv2.PhaseCorrelate(refF, tgtF, window, out _);
        return (shift.X, shift.Y);
    }

    /// <summary>
    /// Translates <paramref name="frame"/> by <paramref name="dx"/>, <paramref name="dy"/> using
    /// <see cref="Cv2.WarpAffine"/> with linear interpolation and replicate borders.
    /// </summary>
    public static Mat AlignFrame(Mat frame, double dx, double dy)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Empty())
            throw new ArgumentException("Frame is empty.", nameof(frame));

        // Matches common OpenCV registration: warp with negative phase-correlation shift.
        using Mat m = new Mat(2, 3, MatType.CV_64FC1);
        m.Set<double>(0, 0, 1);
        m.Set<double>(0, 1, 0);
        m.Set<double>(0, 2, -dx);
        m.Set<double>(1, 0, 0);
        m.Set<double>(1, 1, 1);
        m.Set<double>(1, 2, -dy);
        var aligned = new Mat();
        Cv2.WarpAffine(frame, aligned, m, frame.Size(), InterpolationFlags.Linear, BorderTypes.Replicate);
        return aligned;
    }

    /// <summary>
    /// Computes shift from <paramref name="reference"/> to <paramref name="target"/> and returns a new aligned <see cref="Mat"/>.
    /// </summary>
    public static Mat AlignToReference(Mat reference, Mat target)
    {
        (double dx, double dy) = ComputeShift(reference, target);
        return AlignFrame(target, dx, dy);
    }
}
