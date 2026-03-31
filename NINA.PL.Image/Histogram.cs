using OpenCvSharp;

namespace NINA.PL.Image;

/// <summary>
/// Histogram bins and basic intensity statistics for an image.
/// </summary>
public sealed class HistogramData
{
    public required int[] Values { get; init; }

    public int Min { get; init; }

    public int Max { get; init; }

    public double Mean { get; init; }

    public double StdDev { get; init; }

    public int PeakBin { get; init; }
}

/// <summary>
/// Grayscale histogram (256 bins) and statistics.
/// </summary>
public static class Histogram
{
    /// <summary>
    /// Builds a 256-bin histogram from a grayscale image, or from the luminance of a color image.
    /// For 16-bit sources, values are scaled into 8-bit range via min–max stretch before binning.
    /// </summary>
    public static HistogramData Compute(Mat image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Empty())
            throw new ArgumentException("Image is empty.", nameof(image));

        using Mat gray = ToGray8ForHistogram(image);

        int[] hist = new int[256];
        unsafe
        {
            int step = (int)gray.Step();
            byte* pBase = (byte*)gray.DataPointer;
            int rows = gray.Rows;
            int cols = gray.Cols;

            for (int y = 0; y < rows; y++)
            {
                byte* row = pBase + y * step;
                for (int x = 0; x < cols; x++)
                {
                    byte v = row[x];
                    hist[v]++;
                }
            }
        }

        int total = gray.Rows * gray.Cols;
        double sum = 0;
        double sumSq = 0;
        int min = 255;
        int max = 0;
        int peakBin = 0;
        int peakCount = 0;

        for (int i = 0; i < 256; i++)
        {
            int c = hist[i];
            if (c == 0)
                continue;
            sum += i * c;
            sumSq += (double)i * i * c;
            if (i < min) min = i;
            if (i > max) max = i;
            if (c > peakCount)
            {
                peakCount = c;
                peakBin = i;
            }
        }

        if (total == 0)
        {
            return new HistogramData
            {
                Values = hist,
                Min = 0,
                Max = 0,
                Mean = 0,
                StdDev = 0,
                PeakBin = 0,
            };
        }

        double mean = sum / total;
        double variance = sumSq / total - mean * mean;
        if (variance < 0)
            variance = 0;
        double std = Math.Sqrt(variance);

        if (min > max)
        {
            min = 0;
            max = 0;
        }

        return new HistogramData
        {
            Values = hist,
            Min = min,
            Max = max,
            Mean = mean,
            StdDev = std,
            PeakBin = peakBin,
        };
    }

    private static Mat ToGray8ForHistogram(Mat image)
    {
        MatType t = image.Type();
        int ch = image.Channels();

        if (t == MatType.CV_8UC1)
            return image.Clone();

        if (t == MatType.CV_8UC3 || t == MatType.CV_8UC4)
        {
            var gray = new Mat();
            if (t == MatType.CV_8UC3)
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
            else
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGRA2GRAY);
            return gray;
        }

        if (t == MatType.CV_16UC1)
        {
            using Mat n = new Mat();
            double min = 0, max = 0;
            Cv2.MinMaxLoc(image, out min, out max);
            if (max <= min)
                image.ConvertTo(n, MatType.CV_8UC1);
            else
                image.ConvertTo(n, MatType.CV_8UC1, 255.0 / (max - min), -min * 255.0 / (max - min));
            return n.Clone();
        }

        if (t == MatType.CV_16UC3)
        {
            using Mat bgr = new Mat();
            Cv2.CvtColor(image, bgr, ColorConversionCodes.RGB2BGR);
            using Mat g16 = new Mat();
            Cv2.CvtColor(bgr, g16, ColorConversionCodes.BGR2GRAY);
            using Mat n = new Mat();
            double min = 0, max = 0;
            Cv2.MinMaxLoc(g16, out min, out max);
            if (max <= min)
                g16.ConvertTo(n, MatType.CV_8UC1);
            else
                g16.ConvertTo(n, MatType.CV_8UC1, 255.0 / (max - min), -min * 255.0 / (max - min));
            return n.Clone();
        }

        if (t == MatType.CV_32FC1)
        {
            var gray = new Mat();
            image.ConvertTo(gray, MatType.CV_8UC1, 255.0);
            return gray;
        }

        if (t == MatType.CV_32FC3)
        {
            using Mat g = new Mat();
            Cv2.CvtColor(image, g, ColorConversionCodes.BGR2GRAY);
            var u8 = new Mat();
            g.ConvertTo(u8, MatType.CV_8UC1, 255.0);
            return u8;
        }

        // Fallback: convert to 32F single channel then to 8U
        using Mat any = new Mat();
        if (ch > 1)
            Cv2.CvtColor(image, any, ColorConversionCodes.BGR2GRAY);
        else
            image.ConvertTo(any, MatType.CV_32FC1);
        var result = new Mat();
        any.ConvertTo(result, MatType.CV_8UC1, 255.0);
        return result;
    }
}
