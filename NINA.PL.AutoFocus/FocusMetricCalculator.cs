using System;
using NINA.PL.Core;
using NINA.PL.Image;
using OpenCvSharp;

namespace NINA.PL.AutoFocus;

/// <summary>
/// Computes focus quality metrics from image data using <see cref="ImageStatistics"/>.
/// </summary>
public static class FocusMetricCalculator
{
    public static double Calculate(Mat image, FocusMetricType metricType)
    {
        ArgumentNullException.ThrowIfNull(image);

        return metricType switch
        {
            FocusMetricType.ContrastSobel => ImageStatistics.ComputeContrast(image),
            FocusMetricType.FourierEnergy => ImageStatistics.ComputeFourierEnergy(image),
            FocusMetricType.BrennerGradient => ImageStatistics.ComputeBrennerGradient(image),
            _ => throw new ArgumentException($"Unsupported focus metric: {metricType}.", nameof(metricType)),
        };
    }
}
