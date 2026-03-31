using OpenCvSharp;

namespace NINA.PL.Image;

/// <summary>
/// Focus and quality metrics for planetary and lunar frames.
/// </summary>
public static class ImageStatistics
{
    /// <summary>
    /// Sum of Sobel gradient magnitudes (higher = stronger edges, useful for focus).
    /// </summary>
    public static double ComputeContrast(Mat image)
    {
        using Mat gray = EnsureGrayFloat(image);
        using Mat gx = new Mat();
        using Mat gy = new Mat();
        Cv2.Sobel(gray, gx, MatType.CV_32F, 1, 0, ksize: 3);
        Cv2.Sobel(gray, gy, MatType.CV_32F, 0, 1, ksize: 3);
        using Mat mag = new Mat();
        Cv2.Magnitude(gx, gy, mag);
        return Cv2.Sum(mag).Val0;
    }

    /// <summary>
    /// Ratio of high-frequency energy to total spectral energy (excluding a small DC neighborhood).
    /// </summary>
    public static double ComputeFourierEnergy(Mat image)
    {
        using Mat gray = EnsureGrayFloat(image);
        int rows = gray.Rows;
        int cols = gray.Cols;

        int m = Cv2.GetOptimalDFTSize(rows);
        int n = Cv2.GetOptimalDFTSize(cols);

        using Mat padded = new Mat();
        Cv2.CopyMakeBorder(gray, padded, 0, m - rows, 0, n - cols, BorderTypes.Constant, Scalar.All(0));

        Mat[] planes = { padded, Mat.Zeros(padded.Size(), MatType.CV_32F) };
        using Mat complex = new Mat();
        Cv2.Merge(planes, complex);
        planes[0].Dispose();
        planes[1].Dispose();

        using Mat dft = new Mat();
        Cv2.Dft(complex, dft, DftFlags.ComplexOutput, 0);

        Mat[] dftPlanes = new Mat[2];
        Cv2.Split(dft, out dftPlanes);
        using Mat real = dftPlanes[0];
        using Mat imag = dftPlanes[1];
        using Mat mag = new Mat();
        Cv2.Magnitude(real, imag, mag);
        Cv2.Pow(mag, 2, mag);

        int cx = n / 2;
        int cy = m / 2;
        double dcRadius = Math.Min(m, n) * 0.02;
        if (dcRadius < 2)
            dcRadius = 2;
        double highRadius = Math.Min(m, n) * 0.15;
        if (highRadius <= dcRadius)
            highRadius = dcRadius + 1;

        double total = 0;
        double high = 0;

        for (int y = 0; y < m; y++)
        {
            for (int x = 0; x < n; x++)
            {
                double dx = x - cx;
                double dy = y - cy;
                double r = Math.Sqrt(dx * dx + dy * dy);
                float v = mag.Get<float>(y, x);
                double e = v;
                if (r > dcRadius)
                    total += e;
                if (r > highRadius)
                    high += e;
            }
        }

        if (total <= 1e-12)
            return 0;
        return high / total;
    }

    /// <summary>
    /// Brenner gradient: sum of squared intensity differences over a two-pixel horizontal lag.
    /// </summary>
    public static double ComputeBrennerGradient(Mat image)
    {
        using Mat gray = EnsureGrayFloat(image);
        int rows = gray.Rows;
        int cols = gray.Cols;
        if (cols < 3)
            return 0;

        double sum = 0;
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols - 2; x++)
            {
                double a = gray.Get<float>(y, x);
                double b = gray.Get<float>(y, x + 2);
                double d = b - a;
                sum += d * d;
            }
        }

        return sum;
    }

    /// <summary>
    /// Variance of the Laplacian — common general-purpose sharpness metric.
    /// </summary>
    public static double ComputeSharpness(Mat image)
    {
        using Mat gray = EnsureGrayFloat(image);
        using Mat lap = new Mat();
        Cv2.Laplacian(gray, lap, MatType.CV_32F, ksize: 3);
        Scalar mean = new Scalar();
        Scalar stddev = new Scalar();
        Cv2.MeanStdDev(lap, out mean, out stddev);
        double variance = stddev.Val0 * stddev.Val0;
        return variance;
    }

    /// <summary>
    /// Estimates FWHM (pixels) of the brightest compact feature using 1D cuts through the global maximum.
    /// Returns <see cref="double.NaN"/> if the estimate is not meaningful.
    /// </summary>
    public static double ComputeFWHM(Mat image)
    {
        using Mat gray = EnsureGrayFloat(image);
        if (gray.Rows < 5 || gray.Cols < 5)
            return double.NaN;

        using Mat work = new Mat();
        Cv2.GaussianBlur(gray, work, new Size(3, 3), 0);

        Cv2.MinMaxLoc(work, out double minVal, out double maxVal, out Point minLoc, out Point maxLoc);
        if (maxVal <= minVal + 1e-6)
            return double.NaN;

        double half = minVal + 0.5 * (maxVal - minVal);
        int mx = maxLoc.X;
        int my = maxLoc.Y;

        double? fwhmX = FwhmAlongRow(work, my, mx, half);
        double? fwhmY = FwhmAlongCol(work, mx, my, half);

        if (fwhmX is null && fwhmY is null)
            return double.NaN;
        if (fwhmX is null)
            return fwhmY!.Value;
        if (fwhmY is null)
            return fwhmX.Value;
        return 0.5 * (fwhmX.Value + fwhmY.Value);
    }

    private static double? FwhmAlongRow(Mat work, int row, int peakCol, double halfLevel)
    {
        int cols = work.Cols;
        if (row < 0 || row >= work.Rows || peakCol < 0 || peakCol >= cols)
            return null;

        double left = InterpolateCrossing(work, row, peakCol, -1, halfLevel);
        double right = InterpolateCrossing(work, row, peakCol, 1, halfLevel);
        if (left is double.NaN || right is double.NaN)
            return null;
        double w = right - left;
        return w > 0 ? w : null;
    }

    private static double? FwhmAlongCol(Mat work, int col, int peakRow, double halfLevel)
    {
        int rows = work.Rows;
        if (col < 0 || col >= work.Cols || peakRow < 0 || peakRow >= rows)
            return null;

        double top = InterpolateCrossingCol(work, col, peakRow, -1, halfLevel);
        double bottom = InterpolateCrossingCol(work, col, peakRow, 1, halfLevel);
        if (top is double.NaN || bottom is double.NaN)
            return null;
        double w = bottom - top;
        return w > 0 ? w : null;
    }

    private static double InterpolateCrossing(Mat work, int row, int startCol, int direction, double level)
    {
        int cols = work.Cols;
        int c = startCol;
        double prev = work.Get<float>(row, c);
        if (prev < level)
            return double.NaN;

        while (true)
        {
            int nc = c + direction;
            if (nc < 0 || nc >= cols)
                return double.NaN;
            double cur = work.Get<float>(row, nc);
            if (cur <= level)
            {
                if (Math.Abs(cur - prev) < 1e-12)
                    return nc;
                double t = (level - prev) / (cur - prev);
                return c + t * direction;
            }

            prev = cur;
            c = nc;
            if (Math.Abs(c - startCol) > cols)
                return double.NaN;
        }
    }

    private static double InterpolateCrossingCol(Mat work, int col, int startRow, int direction, double level)
    {
        int rows = work.Rows;
        int r = startRow;
        double prev = work.Get<float>(r, col);
        if (prev < level)
            return double.NaN;

        while (true)
        {
            int nr = r + direction;
            if (nr < 0 || nr >= rows)
                return double.NaN;
            double cur = work.Get<float>(nr, col);
            if (cur <= level)
            {
                if (Math.Abs(cur - prev) < 1e-12)
                    return nr;
                double t = (level - prev) / (cur - prev);
                return r + t * direction;
            }

            prev = cur;
            r = nr;
            if (Math.Abs(r - startRow) > rows)
                return double.NaN;
        }
    }

    private static Mat EnsureGrayFloat(Mat image)
    {
        MatType t = image.Type();
        if (t == MatType.CV_32FC1)
            return image.Clone();

        var f = new Mat();
        if (t == MatType.CV_8UC1)
        {
            image.ConvertTo(f, MatType.CV_32F, 1.0 / 255.0);
            return f;
        }

        if (t == MatType.CV_8UC3)
        {
            using Mat g = new Mat();
            Cv2.CvtColor(image, g, ColorConversionCodes.BGR2GRAY);
            g.ConvertTo(f, MatType.CV_32F, 1.0 / 255.0);
            return f;
        }

        if (t == MatType.CV_8UC4)
        {
            using Mat g = new Mat();
            Cv2.CvtColor(image, g, ColorConversionCodes.BGRA2GRAY);
            g.ConvertTo(f, MatType.CV_32F, 1.0 / 255.0);
            return f;
        }

        if (t == MatType.CV_16UC1)
        {
            image.ConvertTo(f, MatType.CV_32F);
            return f;
        }

        if (t == MatType.CV_16UC3)
        {
            using Mat g = new Mat();
            Cv2.CvtColor(image, g, ColorConversionCodes.RGB2GRAY);
            g.ConvertTo(f, MatType.CV_32F);
            return f;
        }

        if (t == MatType.CV_32FC3)
        {
            using Mat g = new Mat();
            Cv2.CvtColor(image, g, ColorConversionCodes.BGR2GRAY);
            g.CopyTo(f);
            return f;
        }

        using Mat fallback = new Mat();
        if (image.Channels() > 1)
            Cv2.CvtColor(image, fallback, ColorConversionCodes.BGR2GRAY);
        else
            image.ConvertTo(fallback, MatType.CV_32FC1);
        fallback.CopyTo(f);
        return f;
    }
}
