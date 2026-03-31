using System.Buffers.Binary;
using System.Text;
using NINA.PL.Core;

namespace NINA.PL.Capture;

/// <summary>
/// Writes SER v3 files: 178-byte header, raw little-endian frame payload, UTC FILETIME trailer (8 bytes per frame, little-endian).
/// </summary>
public sealed class SERWriter : IDisposable
{
    public const int HeaderSize = 178;
    private const int FrameCountOffset = 38;
    private const int ColorIdOffset = 18;
    private const string FileId = "LUCAM-RECORDER";

    private readonly FileStream _stream;
    private readonly BinaryWriter _writer;
    private readonly int _width;
    private readonly int _height;
    private readonly int _pixelDepth;
    private readonly int _bytesPerFrame;
    private readonly MemoryStream _timestampScratch = new(capacity: 65536);
    private int _frameCount;
    private bool _disposed;

    public int Width => _width;
    public int Height => _height;
    public int FrameCount => _frameCount;

    /// <param name="filename">Output path.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="pixelDepth">8 or 16 bits per sample.</param>
    /// <param name="isColor">True for packed RGB (24-bit at 8bpp, 48-bit at 16bpp). False for single-plane mono or Bayer.</param>
    /// <param name="observer">Observer (ASCII, stored in 40 bytes).</param>
    /// <param name="instrument">Instrument (40 bytes).</param>
    /// <param name="telescope">Telescope (40 bytes).</param>
    public SERWriter(
        string filename,
        int width,
        int height,
        int pixelDepth,
        bool isColor,
        string observer,
        string instrument,
        string telescope)
    {
        ArgumentException.ThrowIfNullOrEmpty(filename);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (pixelDepth is not 8 and not 16)
            throw new ArgumentOutOfRangeException(nameof(pixelDepth), "Must be 8 or 16.");

        _width = width;
        _height = height;
        _pixelDepth = pixelDepth;

        int colorId = isColor ? 100 : 0;
        _bytesPerFrame = isColor
            ? width * height * (pixelDepth == 8 ? 3 : 6)
            : width * height * (pixelDepth == 8 ? 1 : 2);

        _stream = new FileStream(
            filename,
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.Read,
            bufferSize: 4 * 1024 * 1024,
            FileOptions.SequentialScan);
        _writer = new BinaryWriter(_stream, Encoding.UTF8, leaveOpen: true);

        WriteHeader(
            luId: 0,
            colorId: colorId,
            littleEndian: 1,
            imageWidth: width,
            imageHeight: height,
            pixelDepthPerPlane: pixelDepth,
            frameCount: 0,
            observer: observer,
            instrument: instrument,
            telescope: telescope,
            dateTimeLocal: DateTime.Now,
            dateTimeUtc: DateTime.UtcNow);
    }

    /// <summary>
    /// SER ColorID: 0 = MONO, 8 = BAYER_RGGB, 100 = RGB, 101 = BGR.
    /// </summary>
    public static int ColorIdFromPixelFormat(PixelFormat format, bool preferBgr = false) =>
        format switch
        {
            PixelFormat.Mono8 or PixelFormat.Mono16 => 0,
            PixelFormat.BayerRG8 or PixelFormat.BayerRG16 => 8,
            PixelFormat.RGB24 or PixelFormat.RGB48 => preferBgr ? 101 : 100,
            PixelFormat.BGR24 => 101,
            PixelFormat.BGRA32 => 101,
            _ => 0,
        };

    /// <summary>
    /// Patches the ColorID field (byte offset 18). Call before the first <see cref="WriteFrame"/> when using Bayer or BGR.
    /// </summary>
    public void SetColorId(int colorId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_frameCount != 0)
            throw new InvalidOperationException("ColorID can only be set before the first frame.");

        long pos = _stream.Position;
        _stream.Seek(ColorIdOffset, SeekOrigin.Begin);
        _writer.Write(colorId);
        _stream.Seek(pos, SeekOrigin.Begin);
    }

    private void WriteHeader(
        int luId,
        int colorId,
        int littleEndian,
        int imageWidth,
        int imageHeight,
        int pixelDepthPerPlane,
        int frameCount,
        string observer,
        string instrument,
        string telescope,
        DateTime dateTimeLocal,
        DateTime dateTimeUtc)
    {
        WriteFixedString(FileId, 14);
        _writer.Write(luId);
        _writer.Write(colorId);
        _writer.Write(littleEndian);
        _writer.Write(imageWidth);
        _writer.Write(imageHeight);
        _writer.Write(pixelDepthPerPlane);
        _writer.Write(frameCount);
        WriteFixedString(observer ?? string.Empty, 40);
        WriteFixedString(instrument ?? string.Empty, 40);
        WriteFixedString(telescope ?? string.Empty, 40);
        _writer.Write(DateTimeToFileTimeUtc(dateTimeLocal));
        _writer.Write(DateTimeToFileTimeUtc(dateTimeUtc));
    }

    private static long DateTimeToFileTimeUtc(DateTime dt)
    {
        if (dt.Kind == DateTimeKind.Unspecified)
            dt = DateTime.SpecifyKind(dt, DateTimeKind.Local);
        return dt.ToFileTimeUtc();
    }

    private void WriteFixedString(string value, int byteLength)
    {
        var bytes = new byte[byteLength];
        int written = Encoding.ASCII.GetBytes(value.AsSpan(), bytes.AsSpan());
        if (written < byteLength)
            bytes[written] = 0;
        _writer.Write(bytes);
    }

    /// <summary>
    /// Writes raw pixel data for one frame and records the UTC timestamp for the SER trailer.
    /// </summary>
    public void WriteFrame(FrameData frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (frame.Width != _width || frame.Height != _height)
            throw new ArgumentException("Frame dimensions do not match SER header.", nameof(frame));

        if (frame.Data.Length < _bytesPerFrame)
            throw new ArgumentException($"Frame data too small: need at least {_bytesPerFrame} bytes.", nameof(frame));

        _writer.Write(frame.Data.AsSpan(0, _bytesPerFrame));

        long ft = DateTimeToFileTimeUtc(EnsureUtc(frame.Timestamp));
        Span<byte> ts = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(ts, ft);
        _timestampScratch.Write(ts);

        _frameCount++;
    }

    private static DateTime EnsureUtc(DateTime ts) =>
        ts.Kind switch
        {
            DateTimeKind.Utc => ts,
            DateTimeKind.Local => ts.ToUniversalTime(),
            _ => DateTime.SpecifyKind(ts, DateTimeKind.Local).ToUniversalTime(),
        };

    private void PatchFrameCount()
    {
        long end = _stream.Position;
        _stream.Seek(FrameCountOffset, SeekOrigin.Begin);
        _writer.Write(_frameCount);
        _stream.Seek(end, SeekOrigin.Begin);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            _writer.Flush();
            PatchFrameCount();
            _timestampScratch.Position = 0;
            _timestampScratch.CopyTo(_stream);
            _writer.Flush();
            _stream.Flush(true);
        }
        finally
        {
            _writer.Dispose();
            _stream.Dispose();
            _timestampScratch.Dispose();
        }
    }
}
