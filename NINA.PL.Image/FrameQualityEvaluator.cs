using OpenCvSharp;

namespace NINA.PL.Image;

/// <summary>
/// Combines contrast and sharpness metrics into a single lucky-imaging quality score in [0,1].
/// </summary>
public static class FrameQualityEvaluator
{
    private const double ContrastNorm = 250_000.0;
    private const double SharpnessNorm = 80.0;

    /// <summary>
    /// Quality in [0,1] from normalized Sobel contrast and Laplacian variance sharpness.
    /// </summary>
    public static double Evaluate(Mat frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Empty())
            return 0;

        double contrast = ImageStatistics.ComputeContrast(frame);
        double sharp = ImageStatistics.ComputeSharpness(frame);

        double c = contrast / (contrast + ContrastNorm);
        double s = sharp / (sharp + SharpnessNorm);

        return Math.Clamp(0.5 * c + 0.5 * s, 0.0, 1.0);
    }

    /// <summary>
    /// Returns true if <see cref="Evaluate"/> is greater than or equal to <paramref name="threshold"/>.
    /// </summary>
    public static bool PassesThreshold(Mat frame, double threshold)
        => Evaluate(frame) >= threshold;
}
