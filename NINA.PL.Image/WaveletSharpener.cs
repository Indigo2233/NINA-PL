using OpenCvSharp;

namespace NINA.PL.Image;

/// <summary>
/// À trous (stationary) wavelet sharpening using a separable B3-spline scaling filter.
/// </summary>
public static class WaveletSharpener
{
    /// <summary>
    /// Decomposes the image into à trous detail layers, scales each by <paramref name="layerWeights"/>,
    /// and reconstructs. Length of <paramref name="layerWeights"/> is the number of wavelet scales.
    /// Weights above 1 emphasize detail (sharpen); below 1 suppress.
    /// </summary>
    public static Mat Sharpen(Mat input, double[] layerWeights)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(layerWeights);
        if (layerWeights.Length == 0)
            throw new ArgumentException("At least one layer weight is required.", nameof(layerWeights));
        if (input.Empty())
            throw new ArgumentException("Input image is empty.", nameof(input));

        MatType originalType = input.Type();
        int channels = input.Channels();

        using Mat work = new Mat();
        if (channels == 1)
            input.ConvertTo(work, MatType.CV_32FC1);
        else if (channels == 3)
            input.ConvertTo(work, MatType.CV_32FC3);
        else if (channels == 4)
        {
            using Mat bgr = new Mat();
            Cv2.CvtColor(input, bgr, ColorConversionCodes.BGRA2BGR);
            bgr.ConvertTo(work, MatType.CV_32FC3);
        }
        else
            input.ConvertTo(work, FloatTypeForChannels(channels));

        using Mat sharpened = DecomposeReconstruct(work, layerWeights);
        Mat output = new Mat();
        sharpened.ConvertTo(output, originalType);
        return output;
    }

    private static MatType FloatTypeForChannels(int channels) => channels switch
    {
        1 => MatType.CV_32FC1,
        2 => MatType.CV_32FC2,
        3 => MatType.CV_32FC3,
        4 => MatType.CV_32FC4,
        _ => throw new NotSupportedException($"Unsupported channel count: {channels}."),
    };

    private static Mat DecomposeReconstruct(Mat input32f, double[] weights)
    {
        if (input32f.Channels() == 1)
            return DecomposeReconstructSingle(input32f, weights);

        Mat[] ch = Cv2.Split(input32f);
        try
        {
            var outs = new Mat[ch.Length];
            for (int i = 0; i < ch.Length; i++)
            {
                outs[i] = DecomposeReconstructSingle(ch[i], weights);
            }

            Mat merged = new Mat();
            Cv2.Merge(outs, merged);
            foreach (Mat m in outs)
                m.Dispose();
            return merged;
        }
        finally
        {
            foreach (Mat c in ch)
                c.Dispose();
        }
    }

    private static Mat DecomposeReconstructSingle(Mat input32f, double[] weights)
    {
        Mat approximation = input32f.Clone();
        Mat[] details = new Mat[weights.Length];

        for (int level = 0; level < weights.Length; level++)
        {
            using Mat kernelRow = BuildB3RowKernel(level);
            using Mat kernelCol = BuildB3ColKernel(level);

            using Mat blurred = new Mat();
            Cv2.SepFilter2D(approximation, blurred, MatType.CV_32F, kernelRow, kernelCol, new Point(-1, -1), 0, BorderTypes.Replicate);

            Mat detail = new Mat();
            Cv2.Subtract(approximation, blurred, detail);
            details[level] = detail;

            approximation.Dispose();
            approximation = blurred.Clone();
        }

        Mat result = approximation;
        for (int level = 0; level < weights.Length; level++)
        {
            using Mat scaled = new Mat();
            details[level].ConvertTo(scaled, MatType.CV_32F, weights[level], 0);
            Mat sum = new Mat();
            Cv2.Add(result, scaled, sum);
            result.Dispose();
            details[level].Dispose();
            result = sum;
        }

        return result;
    }

    /// <summary>
    /// B3 spline (cubic) scaling coefficients normalized to sum 1: (1,4,6,4,1)/16, spaced by 2^level.
    /// </summary>
    private static Mat BuildB3RowKernel(int level)
    {
        int step = 1 << level;
        const float w0 = 1f / 16f;
        const float w1 = 4f / 16f;
        const float w2 = 6f / 16f;
        int maxOff = 2 * step;
        int size = 2 * maxOff + 1;
        Mat k = new Mat(1, size, MatType.CV_32F, Scalar.All(0));
        int c = maxOff;
        k.Set<float>(0, c - 2 * step, w0);
        k.Set<float>(0, c - step, w1);
        k.Set<float>(0, c, w2);
        k.Set<float>(0, c + step, w1);
        k.Set<float>(0, c + 2 * step, w0);
        return k;
    }

    private static Mat BuildB3ColKernel(int level)
    {
        using Mat row = BuildB3RowKernel(level);
        Mat col = new Mat(row.Cols, 1, MatType.CV_32F);
        for (int i = 0; i < row.Cols; i++)
            col.Set<float>(i, 0, row.Get<float>(0, i));
        return col;
    }
}
