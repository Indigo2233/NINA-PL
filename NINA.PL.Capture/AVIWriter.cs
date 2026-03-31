using System.Text;

namespace NINA.PL.Capture;

/// <summary>
/// Uncompressed AVI 1.0 (RGB24, BI_RGB) writer using the RIFF structure and an idx1 index.
/// </summary>
public sealed class AVIWriter : IDisposable
{
    private const uint AvifHasIndex = 0x00000010;
    private const uint StreamTypeVids = 0x73646976; // 'vids'
    private const uint DibHandler = 0x20424944; // 'DIB '

    private readonly FileStream _stream;
    private readonly BinaryWriter _writer;
    private readonly int _width;
    private readonly int _height;
    private readonly int _stride;
    private readonly int _frameSize;
    private readonly uint _usPerFrame;
    private readonly uint _rate;
    private readonly long _riffSizePatch;
    private readonly long _hdrlListSizePatch;
    private readonly long _moviListSizePatch;
    private readonly long _avihFrameCountPatch;
    private readonly long _strhLengthPatch;
    private readonly long _hdrlAfterListFourCc;
    private readonly List<(long ChunkStart, int ChunkDataSize)> _index = new();
    private int _frameCount;
    private long _moviPayloadStart;
    private bool _disposed;

    public int Width => _width;
    public int Height => _height;
    public int FrameCount => _frameCount;

    /// <param name="filename">Output path.</param>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    /// <param name="fps">Frames per second (&gt; 0).</param>
    public AVIWriter(string filename, int width, int height, double fps)
    {
        ArgumentException.ThrowIfNullOrEmpty(filename);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (fps <= 0 || double.IsNaN(fps) || double.IsInfinity(fps))
            throw new ArgumentOutOfRangeException(nameof(fps));

        _width = width;
        _height = height;
        _stride = ((width * 24 + 31) / 32) * 4;
        _frameSize = _stride * height;
        _usPerFrame = (uint)Math.Clamp(Math.Round(1_000_000.0 / fps), 1, uint.MaxValue);
        _rate = (uint)Math.Max(1, Math.Round(fps));

        _stream = new FileStream(
            filename,
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.Read,
            bufferSize: 4 * 1024 * 1024,
            FileOptions.SequentialScan);
        _writer = new BinaryWriter(_stream, Encoding.UTF8, leaveOpen: true);

        _writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        _riffSizePatch = _stream.Position;
        _writer.Write(0);
        _writer.Write(Encoding.ASCII.GetBytes("AVI "));

        _writer.Write(Encoding.ASCII.GetBytes("LIST"));
        _hdrlListSizePatch = _stream.Position;
        _writer.Write(0);
        _writer.Write(Encoding.ASCII.GetBytes("hdrl"));
        _hdrlAfterListFourCc = _stream.Position;

        _writer.Write(Encoding.ASCII.GetBytes("avih"));
        _writer.Write(56);
        _avihFrameCountPatch = _stream.Position + 16;
        WriteMainAviHeaderPlaceholder();

        _writer.Write(Encoding.ASCII.GetBytes("LIST"));
        long strlListSizePatch = _stream.Position;
        _writer.Write(0);
        _writer.Write(Encoding.ASCII.GetBytes("strl"));

        long strlPayloadStart = _stream.Position;

        _writer.Write(Encoding.ASCII.GetBytes("strh"));
        _writer.Write(56);
        _strhLengthPatch = _stream.Position + 32;
        WriteVideoStreamHeaderPlaceholder();

        _writer.Write(Encoding.ASCII.GetBytes("strf"));
        _writer.Write(40);
        WriteBitmapInfoHeader();

        long strlEnd = _stream.Position;
        uint strlListSize = (uint)(strlEnd - strlPayloadStart + 4);
        _stream.Seek(strlListSizePatch, SeekOrigin.Begin);
        _writer.Write(strlListSize);
        _stream.Seek(strlEnd, SeekOrigin.Begin);

        uint hdrlListSize = (uint)(_stream.Position - _hdrlAfterListFourCc);
        _stream.Seek(_hdrlListSizePatch, SeekOrigin.Begin);
        _writer.Write(hdrlListSize);
        _stream.Seek(strlEnd, SeekOrigin.Begin);

        _writer.Write(Encoding.ASCII.GetBytes("LIST"));
        _moviListSizePatch = _stream.Position;
        _writer.Write(0);
        _writer.Write(Encoding.ASCII.GetBytes("movi"));
        _moviPayloadStart = _stream.Position;
    }

    private void WriteMainAviHeaderPlaceholder()
    {
        _writer.Write(_usPerFrame);
        _writer.Write((uint)(_frameSize * Math.Max(1, _rate)));
        _writer.Write(0u);
        _writer.Write(AvifHasIndex);
        _writer.Write(0u);
        _writer.Write(1u);
        _writer.Write((uint)_frameSize);
        _writer.Write((uint)_width);
        _writer.Write((uint)_height);
        for (int i = 0; i < 4; i++)
            _writer.Write(0u);
    }

    private void WriteVideoStreamHeaderPlaceholder()
    {
        _writer.Write(StreamTypeVids);
        _writer.Write(DibHandler);
        _writer.Write(0u);
        _writer.Write((ushort)0);
        _writer.Write((ushort)0);
        _writer.Write(0u);
        _writer.Write(1u);
        _writer.Write(_rate);
        _writer.Write(0u);
        _writer.Write(0u);
        _writer.Write((uint)_frameSize);
        _writer.Write(uint.MaxValue);
        _writer.Write(0u);
        _writer.Write((short)0);
        _writer.Write((short)0);
        _writer.Write((short)_width);
        _writer.Write((short)_height);
    }

    private void WriteBitmapInfoHeader()
    {
        _writer.Write(40);
        _writer.Write(_width);
        _writer.Write(-_height);
        _writer.Write((ushort)1);
        _writer.Write((ushort)24);
        _writer.Write(0);
        _writer.Write((uint)(_stride * _height));
        _writer.Write(0);
        _writer.Write(0);
        _writer.Write(0u);
        _writer.Write(0u);
    }

    /// <summary>
    /// Writes one top-down RGB24 frame. <paramref name="rgbData"/> may be tightly packed (width * height * 3);
    /// rows are padded to the AVI DIB stride automatically.
    /// </summary>
    public void WriteFrame(byte[] rgbData)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int packed = _width * _height * 3;
        if (rgbData.Length < packed)
            throw new ArgumentException($"Expected at least {packed} bytes of RGB data.", nameof(rgbData));

        int chunkDataSize = _frameSize;

        long chunkStart = _stream.Position;
        _writer.Write(Encoding.ASCII.GetBytes("00db"));
        _writer.Write(chunkDataSize);

        if (_stride == _width * 3)
        {
            _writer.Write(rgbData.AsSpan(0, packed));
        }
        else
        {
            int rowPacked = _width * 3;
            for (int y = 0; y < _height; y++)
            {
                ReadOnlySpan<byte> row = rgbData.AsSpan(y * rowPacked, rowPacked);
                _writer.Write(row);
                int pad = _stride - rowPacked;
                for (int p = 0; p < pad; p++)
                    _writer.Write((byte)0);
            }
        }

        if ((chunkDataSize & 1) != 0)
            _writer.Write((byte)0);

        _index.Add((chunkStart, chunkDataSize));
        _frameCount++;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            long fileEnd = _stream.Position;

            long idx1PayloadStart = fileEnd;
            _writer.Write(Encoding.ASCII.GetBytes("idx1"));
            long idx1SizePatch = _stream.Position;
            _writer.Write(0);

            const uint AviOldIndexChunk = 0x00000010;
            foreach (var (chunkStart, chunkDataSize) in _index)
            {
                _writer.Write(Encoding.ASCII.GetBytes("00db"));
                _writer.Write(AviOldIndexChunk);
                _writer.Write((uint)(chunkStart - _moviPayloadStart));
                _writer.Write((uint)chunkDataSize);
            }

            long idx1End = _stream.Position;
            uint idx1Size = (uint)(idx1End - idx1PayloadStart - 8);
            _stream.Seek(idx1SizePatch, SeekOrigin.Begin);
            _writer.Write(idx1Size);
            _stream.Seek(idx1End, SeekOrigin.Begin);

            long moviEnd = idx1PayloadStart;
            uint moviListSize = (uint)(moviEnd - _moviListSizePatch - 4);
            _stream.Seek(_moviListSizePatch, SeekOrigin.Begin);
            _writer.Write(moviListSize);
            _stream.Seek(idx1End, SeekOrigin.Begin);

            _stream.Seek(_avihFrameCountPatch, SeekOrigin.Begin);
            _writer.Write((uint)_frameCount);
            _stream.Seek(_strhLengthPatch, SeekOrigin.Begin);
            _writer.Write((uint)_frameCount);
            _stream.Seek(idx1End, SeekOrigin.Begin);

            uint riffSize = (uint)(_stream.Length - 8);
            _stream.Seek(_riffSizePatch, SeekOrigin.Begin);
            _writer.Write(riffSize);

            _stream.Seek(0, SeekOrigin.End);
            _writer.Flush();
            _stream.Flush(true);
        }
        finally
        {
            _writer.Dispose();
            _stream.Dispose();
        }
    }
}
