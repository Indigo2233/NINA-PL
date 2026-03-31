using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NINA.PL.Core;
using NINA.PL.Image;
using OpenCvSharp;

namespace NINA.PL.WPF.Helpers;

public static class MatBitmapHelper
{
    public static WriteableBitmap? FrameToWriteableBitmap(FrameData frame)
    {
        using Mat mat = Debayer.ToMat(frame);
        return MatToWriteableBitmap(mat);
    }

    public static WriteableBitmap? MatToWriteableBitmap(Mat? mat)
    {
        if (mat is null || mat.Empty())
            return null;

        Mat work = mat;
        Mat? converted = null;
        try
        {
            if (mat.Type() != MatType.CV_8UC1 && mat.Type() != MatType.CV_8UC3)
            {
                converted = new Mat();
                mat.ConvertTo(converted, mat.Channels() == 1 ? MatType.CV_8UC1 : MatType.CV_8UC3);
                work = converted;
            }

            int w = work.Cols;
            int h = work.Rows;
            System.Windows.Media.PixelFormat fmt = work.Channels() == 1 ? PixelFormats.Gray8 : PixelFormats.Bgr24;
            int stride = fmt == PixelFormats.Gray8 ? w : w * 3;
            int copyBytes = stride * h;

            var wb = new WriteableBitmap(w, h, 96, 96, fmt, null);
            wb.Lock();
            try
            {
                long step = work.Step();
                var buffer = new byte[copyBytes];
                if (step == stride)
                {
                    Marshal.Copy(work.Data, buffer, 0, copyBytes);
                }
                else
                {
                    int dst = 0;
                    for (int y = 0; y < h; y++)
                    {
                        Marshal.Copy(work.Ptr(y), buffer, dst, stride);
                        dst += stride;
                    }
                }

                Marshal.Copy(buffer, 0, wb.BackBuffer, copyBytes);
                wb.AddDirtyRect(new Int32Rect(0, 0, w, h));
            }
            finally
            {
                wb.Unlock();
            }

            return wb;
        }
        finally
        {
            converted?.Dispose();
        }
    }
}
