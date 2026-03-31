using System.Globalization;
using System.Text;
using NINA.PL.Core;

namespace NINA.PL.Capture;

/// <summary>
/// Writes a single-frame FITS primary HDU with 2880-byte block alignment.
/// </summary>
public static class FITSWriter
{
    private const int CardLength = 80;
    private const int BlockSize = 2880;

    /// <summary>
    /// Writes one FITS file for <paramref name="frame"/> into <paramref name="directory"/>.
    /// </summary>
    /// <param name="directory">Output directory (created if missing).</param>
    /// <param name="frame">Frame payload and metadata.</param>
    /// <param name="extraHeaders">Optional keyword/value pairs (ASCII-safe values).</param>
    public static string WriteFrame(string directory, FrameData frame, Dictionary<string, object>? extraHeaders = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentNullException.ThrowIfNull(frame);

        Directory.CreateDirectory(directory);

        string dateStamp = frame.Timestamp.ToUniversalTime().ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        string fileName = $"{SanitizeFileNamePart(dateStamp)}_{frame.FrameId:D10}.fits";
        string path = Path.Combine(directory, fileName);

        var cards = new List<string>();
        cards.Add(MakeCard("SIMPLE", true, "Standard FITS"));
        cards.Add(MakeCard("BITPIX", GetBitpix(frame.PixelFormat), "Bits per pixel"));
        cards.Add(MakeCard("NAXIS", GetNaxis(frame.PixelFormat), "Number of axes"));

        switch (frame.PixelFormat)
        {
            case PixelFormat.RGB24:
            case PixelFormat.RGB48:
                cards.Add(MakeCard("NAXIS1", 3, "RGB along fastest axis"));
                cards.Add(MakeCard("NAXIS2", frame.Width, "Width"));
                cards.Add(MakeCard("NAXIS3", frame.Height, "Height"));
                break;
            case PixelFormat.BGR24:
                cards.Add(MakeCard("NAXIS1", 3, "BGR along fastest axis"));
                cards.Add(MakeCard("NAXIS2", frame.Width, "Width"));
                cards.Add(MakeCard("NAXIS3", frame.Height, "Height"));
                break;
            case PixelFormat.BGRA32:
                cards.Add(MakeCard("NAXIS1", 4, "BGRA along fastest axis"));
                cards.Add(MakeCard("NAXIS2", frame.Width, "Width"));
                cards.Add(MakeCard("NAXIS3", frame.Height, "Height"));
                break;
            default:
                cards.Add(MakeCard("NAXIS1", frame.Width, "Width"));
                cards.Add(MakeCard("NAXIS2", frame.Height, "Height"));
                break;
        }

        cards.Add(MakeCard("EXTEND", false, "No extensions"));

        if (frame.PixelFormat is PixelFormat.BayerRG8 or PixelFormat.BayerRG16)
            cards.Add(MakeCard("BAYERPAT", "RGGB", "Bayer pattern"));

        cards.Add(MakeCard("EXPOSURE", frame.ExposureUs * 1e-6, "Exposure time (s)"));
        cards.Add(MakeCard("GAIN", frame.Gain, "Camera gain"));
        cards.Add(MakeCard("DATE-OBS", FormatDateObs(frame.Timestamp), "UTC start of exposure"));
        cards.Add(MakeCard("FRAME-ID", frame.FrameId.ToString(CultureInfo.InvariantCulture), "Source frame id"));

        if (GetBitpix(frame.PixelFormat) == 16)
        {
            cards.Add(MakeCard("BSCALE", 1.0, "Real = BSCALE * pixel + BZERO"));
            cards.Add(MakeCard("BZERO", 32768.0, "Unsigned 16 interpretation"));
        }

        if (extraHeaders is not null)
        {
            foreach (var kv in extraHeaders)
            {
                if (string.IsNullOrWhiteSpace(kv.Key))
                    continue;
                cards.Add(MakeCardFromObject(kv.Key, kv.Value));
            }
        }

        cards.Add("END".PadRight(CardLength));

        int headerBytes = ((cards.Count * CardLength + BlockSize - 1) / BlockSize) * BlockSize;
        var header = new byte[headerBytes];
        int offset = 0;
        foreach (string card in cards)
        {
            WriteCard(header.AsSpan(offset, CardLength), card);
            offset += CardLength;
        }

        ReadOnlySpan<byte> raw = frame.Data;
        int expected = GetExpectedByteLength(frame);
        if (raw.Length < expected)
            throw new ArgumentException($"Frame data too small for {frame.PixelFormat}: need {expected} bytes.", nameof(frame));
        raw = raw.Slice(0, expected);

        byte[] imageBe = ConvertImageToBigEndian(raw, frame.PixelFormat);
        int dataBytes = imageBe.Length;
        int paddedData = ((dataBytes + BlockSize - 1) / BlockSize) * BlockSize;
        var dataBlock = new byte[paddedData];
        imageBe.AsSpan().CopyTo(dataBlock);

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        fs.Write(header);
        fs.Write(dataBlock);
        fs.Flush(true);

        return path;
    }

    private static string SanitizeFileNamePart(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s;
    }

    private static int GetBitpix(PixelFormat pf) =>
        pf switch
        {
            PixelFormat.Mono8 or PixelFormat.BayerRG8 or PixelFormat.RGB24 or PixelFormat.BGR24 or PixelFormat.BGRA32 => 8,
            PixelFormat.Mono16 or PixelFormat.BayerRG16 or PixelFormat.RGB48 => 16,
            _ => 8,
        };

    private static int GetNaxis(PixelFormat pf) =>
        pf is PixelFormat.RGB24 or PixelFormat.RGB48 or PixelFormat.BGR24 or PixelFormat.BGRA32 ? 3 : 2;

    private static int GetExpectedByteLength(FrameData frame) =>
        frame.PixelFormat switch
        {
            PixelFormat.Mono8 or PixelFormat.BayerRG8 => frame.Width * frame.Height,
            PixelFormat.Mono16 or PixelFormat.BayerRG16 => frame.Width * frame.Height * 2,
            PixelFormat.RGB24 => frame.Width * frame.Height * 3,
            PixelFormat.RGB48 => frame.Width * frame.Height * 6,
            PixelFormat.BGR24 => frame.Width * frame.Height * 3,
            PixelFormat.BGRA32 => frame.Width * frame.Height * 4,
            _ => frame.Width * frame.Height,
        };

    private static byte[] ConvertImageToBigEndian(ReadOnlySpan<byte> raw, PixelFormat pf)
    {
        if (pf is not (PixelFormat.Mono16 or PixelFormat.BayerRG16 or PixelFormat.RGB48))
            return raw.ToArray();

        var dst = new byte[raw.Length];
        if (pf is PixelFormat.RGB48)
        {
            for (int i = 0; i + 5 < raw.Length; i += 6)
            {
                dst[i] = raw[i + 1];
                dst[i + 1] = raw[i];
                dst[i + 2] = raw[i + 3];
                dst[i + 3] = raw[i + 2];
                dst[i + 4] = raw[i + 5];
                dst[i + 5] = raw[i + 4];
            }
        }
        else
        {
            for (int i = 0; i + 1 < raw.Length; i += 2)
            {
                dst[i] = raw[i + 1];
                dst[i + 1] = raw[i];
            }
        }

        return dst;
    }

    private static string FormatDateObs(DateTime ts)
    {
        DateTime u = ts.Kind switch
        {
            DateTimeKind.Utc => ts,
            DateTimeKind.Local => ts.ToUniversalTime(),
            _ => DateTime.SpecifyKind(ts, DateTimeKind.Local).ToUniversalTime(),
        };
        return u.ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture);
    }

    private static string MakeCardFromObject(string keyword, object value) =>
        value switch
        {
            bool b => MakeCard(keyword, b, null),
            byte or sbyte or short or ushort or int or uint or long => MakeCard(keyword, Convert.ToInt64(value), null),
            ulong ul => MakeCard(keyword, ul.ToString(CultureInfo.InvariantCulture), null),
            float or double or decimal => MakeCard(keyword, Convert.ToDouble(value, CultureInfo.InvariantCulture), null),
            string s => MakeCard(keyword, s, null),
            DateTime dt => MakeCard(keyword, FormatDateObs(dt), null),
            _ => MakeCard(keyword, value.ToString() ?? string.Empty, null),
        };

    private static string MakeCard(string keyword, bool value, string? comment)
    {
        string v = value ? "T" : "F";
        return FormatCard(keyword, v, comment, isString: false);
    }

    private static string MakeCard(string keyword, int value, string? comment) =>
        FormatCard(keyword, value.ToString(CultureInfo.InvariantCulture), comment, isString: false);

    private static string MakeCard(string keyword, long value, string? comment) =>
        FormatCard(keyword, value.ToString(CultureInfo.InvariantCulture), comment, isString: false);

    private static string MakeCard(string keyword, double value, string? comment)
    {
        string s = value.ToString("G15", CultureInfo.InvariantCulture);
        return FormatCard(keyword, s, comment, isString: false);
    }

    private static string MakeCard(string keyword, string value, string? comment)
    {
        string escaped = value.Replace("'", "''");
        if (escaped.Length > 68)
            escaped = escaped[..68];
        return FormatCard(keyword, $"'{escaped}'", comment, isString: true);
    }

    private static string FormatCard(string keyword, string value, string? comment, bool isString)
    {
        if (keyword.Length > 8)
            keyword = keyword[..8];

        string basePart = $"{keyword,-8}= ";
        string commentPart = comment is null ? string.Empty : $" / {comment}";
        int maxValueLen = CardLength - basePart.Length - commentPart.Length;
        if (maxValueLen < 1)
            commentPart = string.Empty;

        if (!isString && value.Length > maxValueLen)
            value = value[..maxValueLen];

        string line = $"{basePart}{value}{commentPart}";
        if (line.Length > CardLength)
            line = line[..CardLength];
        return line.PadRight(CardLength);
    }

    private static void WriteCard(Span<byte> dest, string cardLine)
    {
        dest.Clear();
        int len = Math.Min(CardLength, cardLine.Length);
        Encoding.ASCII.GetBytes(cardLine.AsSpan(0, len), dest[..len]);
    }
}
