using NINA.PL.Core;
using OpenCvSharp;

namespace NINA.PL.Image;

/// <summary>
/// Bayer demosaicing and <see cref="FrameData"/> to <see cref="Mat"/> conversion using OpenCvSharp.
/// </summary>
public static class Debayer
{
    /// <summary>
    /// Demosaics Bayer raw frames to BGR; passes through mono; reshapes RGB layouts to BGR <see cref="Mat"/>.
    /// (C# does not allow a method named the same as the enclosing type; use this entry point for debayering.)
    /// </summary>
    public static Mat Demosaic(FrameData frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        using var src = CreateSourceMat(frame);

        return frame.PixelFormat switch
        {
            PixelFormat.BayerRG8 => Demosaic8(src, ColorConversionCodes.BayerRG2BGR),
            PixelFormat.BayerRG16 => Demosaic16(src, ColorConversionCodes.BayerRG2BGR),
            PixelFormat.Mono8 or PixelFormat.Mono16 => src.Clone(),
            PixelFormat.RGB24 => ToBgrFromRgb24(src),
            PixelFormat.RGB48 => ToBgrFromRgb48(src),
            PixelFormat.BGR24 => src.Clone(),
            PixelFormat.BGRA32 => ToBgrFromBgra(src),
            _ => throw new NotSupportedException($"Pixel format {frame.PixelFormat} is not supported for debayering."),
        };
    }

    /// <summary>
    /// Packed top-down RGB24 (<c>width * height * 3</c>) for uncompressed AVI or similar sinks.
    /// </summary>
    public static byte[] ToPackedRgb24(FrameData frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        using Mat bgr = ToMat(frame);
        using var rgb = new Mat();
        if (bgr.Channels() == 1)
        {
            using var tmp = new Mat();
            Cv2.CvtColor(bgr, tmp, ColorConversionCodes.GRAY2BGR);
            Cv2.CvtColor(tmp, rgb, ColorConversionCodes.BGR2RGB);
        }
        else
        {
            Cv2.CvtColor(bgr, rgb, ColorConversionCodes.BGR2RGB);
        }

        return rgb.ToBytes();
    }

    /// <summary>
    /// Converts any supported <see cref="FrameData"/> to a display-oriented <see cref="Mat"/> (8-bit BGR or 8-bit gray).
    /// </summary>
    public static Mat ToMat(FrameData frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        using var src = CreateSourceMat(frame);

        return frame.PixelFormat switch
        {
            PixelFormat.BayerRG8 => Demosaic8(src, ColorConversionCodes.BayerRG2BGR),
            PixelFormat.BayerRG16 => Demosaic16To8(src, ColorConversionCodes.BayerRG2BGR),
            PixelFormat.Mono8 => src.Clone(),
            PixelFormat.Mono16 => Normalize16To8Gray(src),
            PixelFormat.RGB24 => ToBgrFromRgb24(src),
            PixelFormat.RGB48 => ToBgrFromRgb48Normalized8(src),
            PixelFormat.BGR24 => src.Clone(),
            PixelFormat.BGRA32 => ToBgrFromBgra(src),
            _ => throw new NotSupportedException($"Pixel format {frame.PixelFormat} is not supported."),
        };
    }

    private static Mat CreateSourceMat(FrameData frame)
    {
        int w = frame.Width;
        int h = frame.Height;
        if (w <= 0 || h <= 0)
            throw new ArgumentException("Width and height must be positive.", nameof(frame));

        byte[] data = frame.Data;
        return frame.PixelFormat switch
        {
            PixelFormat.Mono8 => MatFromBytes(h, w, MatType.CV_8UC1, data),
            PixelFormat.Mono16 => MatFromBytes(h, w, MatType.CV_16UC1, data),
            PixelFormat.BayerRG8 => MatFromBytes(h, w, MatType.CV_8UC1, data),
            PixelFormat.BayerRG16 => MatFromBytes(h, w, MatType.CV_16UC1, data),
            PixelFormat.RGB24 => MatFromBytes(h, w, MatType.CV_8UC3, data),
            PixelFormat.RGB48 => MatFromBytes(h, w, MatType.CV_16UC3, data),
            PixelFormat.BGR24 => MatFromBytes(h, w, MatType.CV_8UC3, data),
            PixelFormat.BGRA32 => MatFromBytes(h, w, MatType.CV_8UC4, data),
            _ => throw new NotSupportedException($"Pixel format {frame.PixelFormat} is not supported."),
        };
    }

    private static Mat MatFromBytes(int rows, int cols, MatType type, byte[] data)
    {
        int elemSize = MatTypeByteSize(type);
        long expected = (long)rows * cols * elemSize;
        if (data.Length != expected)
            throw new ArgumentException($"Buffer length {data.Length} does not match {rows}x{cols} {type} (expected {expected} bytes).");

        var mat = new Mat(rows, cols, type);
        long step = (long)cols * elemSize;
        using (Mat tmp = Mat.FromPixelData(rows, cols, type, data, step))
            tmp.CopyTo(mat);

        return mat;
    }

    private static int MatTypeByteSize(MatType type)
    {
        using var probe = new Mat(1, 1, type);
        return (int)probe.ElemSize();
    }

    private static Mat Demosaic8(Mat src, ColorConversionCodes code)
    {
        var dst = new Mat();
        Cv2.CvtColor(src, dst, code);
        return dst;
    }

    private static Mat Demosaic16(Mat src, ColorConversionCodes code)
    {
        var dst = new Mat();
        Cv2.CvtColor(src, dst, code);
        return dst;
    }

    private static Mat Demosaic16To8(Mat src, ColorConversionCodes code)
    {
        using Mat bgr16 = new Mat();
        Cv2.CvtColor(src, bgr16, code);
        var dst = new Mat();
        bgr16.ConvertTo(dst, MatType.CV_8UC3, 1.0 / 256.0);
        return dst;
    }

    private static Mat ToBgrFromRgb24(Mat src)
    {
        var dst = new Mat();
        Cv2.CvtColor(src, dst, ColorConversionCodes.RGB2BGR);
        return dst;
    }

    private static Mat ToBgrFromRgb48(Mat src)
    {
        var dst = new Mat();
        Cv2.CvtColor(src, dst, ColorConversionCodes.RGB2BGR);
        return dst;
    }

    private static Mat ToBgrFromRgb48Normalized8(Mat src)
    {
        using Mat bgr16 = new Mat();
        Cv2.CvtColor(src, bgr16, ColorConversionCodes.RGB2BGR);
        var dst = new Mat();
        bgr16.ConvertTo(dst, MatType.CV_8UC3, 1.0 / 256.0);
        return dst;
    }

    private static Mat ToBgrFromBgra(Mat src)
    {
        var dst = new Mat();
        Cv2.CvtColor(src, dst, ColorConversionCodes.BGRA2BGR);
        return dst;
    }

    private static Mat Normalize16To8Gray(Mat src16)
    {
        var dst = new Mat();
        double min = 0, max = 0;
        Cv2.MinMaxLoc(src16, out min, out max);
        if (max <= min)
        {
            src16.ConvertTo(dst, MatType.CV_8UC1);
            return dst;
        }

        src16.ConvertTo(dst, MatType.CV_8UC1, 255.0 / (max - min), -min * 255.0 / (max - min));
        return dst;
    }
}
