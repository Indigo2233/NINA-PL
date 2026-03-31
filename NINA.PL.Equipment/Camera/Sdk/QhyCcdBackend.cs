using System.Runtime.InteropServices;
using System.Text;

namespace NINA.PL.Equipment.Camera.Sdk;

/// <summary>QHYCCD cameras via <c>qhyccd.dll</c> (live mode + <c>GetQHYCCDLiveFrame</c> polling).</summary>
public sealed class QhyCcdBackend : INativeCameraBackend
{
    private const string DllName = "qhyccd";
    private const uint QhyccdSuccess = 0;

    private static readonly object s_qhyResourceLock = new();
    private static int s_qhyResourceRef;

    private bool _initialized;
    private bool _dllMissing;
    private bool _ownsResourceSlice;
    private nint _handle = nint.Zero;
    private string _openedId = string.Empty;
    private readonly object _gate = new();
    private Thread? _liveThread;
    private volatile bool _captureRunning;
    private ulong _frameSeq;
    private uint _memLength;
    private int _width;
    private int _height;
    private uint _bpp = 8;
    private double _expMin;
    private double _expMax;
    private double _gainMin;
    private double _gainMax;
    private int _maxBin = 4;
    private string _modelName = string.Empty;
    private string _sensorName = string.Empty;
    private bool _isColor;
    private string _bayerPattern = string.Empty;
    private double _pixelW;
    private double _pixelH;
    private bool _disposed;

    private enum QhyControlId
    {
        CONTROL_GAIN = 6,
        CONTROL_EXPOSURE = 8,
        CONTROL_TRANSFERBIT = 10,
        CAM_IS_COLOR = 59
    }

    public event EventHandler<NativeFrameData>? FrameArrived;

    public bool IsConnected => _handle != nint.Zero;

    public int SensorWidth => _width;

    public int SensorHeight => _height;

    public double ExposureMin => _expMin;

    public double ExposureMax => _expMax;

    public double GainMin => _gainMin;

    public double GainMax => _gainMax;

    public int MaxBinX => _maxBin;

    public int MaxBinY => _maxBin;

    public double PixelSizeUm => _pixelW > 0 ? _pixelW : _pixelH;

    public string ModelName => _modelName;

    public string SensorName => _sensorName;

    public bool IsColorCamera => _isColor;

    public string BayerPattern => _bayerPattern;

    public void Initialize()
    {
        lock (_gate)
        {
            if (_initialized)
                return;
            _initialized = true;
            try
            {
                lock (s_qhyResourceLock)
                {
                    if (s_qhyResourceRef == 0)
                    {
                        if (NativeQhy.InitQHYCCDResource() != QhyccdSuccess)
                        {
                            _dllMissing = true;
                            return;
                        }
                    }

                    s_qhyResourceRef++;
                    _ownsResourceSlice = true;
                }
            }
            catch (DllNotFoundException)
            {
                _dllMissing = true;
            }
            catch (BadImageFormatException)
            {
                _dllMissing = true;
            }
        }
    }

    public List<NativeCameraInfo> EnumerateCameras()
    {
        if (_dllMissing)
            return new List<NativeCameraInfo>();

        try
        {
            var n = NativeQhy.ScanQHYCCD();
            var list = new List<NativeCameraInfo>();
            for (uint i = 0; i < n; i++)
            {
                var idBuf = new byte[64];
                if (NativeQhy.GetQHYCCDId(i, idBuf) != QhyccdSuccess)
                    continue;
                var id = NullTerminatedAscii(idBuf);
                if (string.IsNullOrEmpty(id))
                    continue;

                var modelBuf = new byte[128];
                var idBytes = Encoding.ASCII.GetBytes(id + "\0");
                _ = NativeQhy.GetQHYCCDModel(idBytes, modelBuf);
                var model = NullTerminatedAscii(modelBuf);

                list.Add(new NativeCameraInfo
                {
                    SerialNumber = id,
                    ModelName = string.IsNullOrEmpty(model) ? id : model,
                    VendorName = "QHYCCD"
                });
            }

            return list;
        }
        catch (DllNotFoundException)
        {
            return new List<NativeCameraInfo>();
        }
    }

    public void OpenCamera(string serialNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serialNumber);
        ThrowIfNoDll();
        var idBytes = Encoding.ASCII.GetBytes(serialNumber + "\0");

        lock (_gate)
        {
            CloseCameraInternal();
            _handle = NativeQhy.OpenQHYCCD(idBytes);
            if (_handle == nint.Zero)
                throw new InvalidOperationException("OpenQHYCCD failed.");

            if (NativeQhy.InitQHYCCD(_handle) != QhyccdSuccess)
            {
                NativeQhy.CloseQHYCCD(_handle);
                _handle = nint.Zero;
                throw new InvalidOperationException("InitQHYCCD failed.");
            }

            _openedId = serialNumber;
            LoadCameraInfo();
            ApplyFullFrame();
            NativeQhy.SetQHYCCDBitsMode(_handle, 8);
            DiscoverControlRanges();
        }
    }

    public void CloseCamera()
    {
        lock (_gate)
            CloseCameraInternal();
    }

    public void SetExposureTime(double microseconds)
    {
        ThrowIfOpen();
        var v = Math.Clamp(microseconds, _expMin, _expMax);
        if (NativeQhy.SetQHYCCDParam(_handle, (int)QhyControlId.CONTROL_EXPOSURE, v) != QhyccdSuccess)
            throw new InvalidOperationException("SetQHYCCDParam(CONTROL_EXPOSURE) failed.");
    }

    public double GetGain()
    {
        ThrowIfOpen();
        return NativeQhy.GetQHYCCDParam(_handle, (int)QhyControlId.CONTROL_GAIN);
    }

    public void SetGain(double gain)
    {
        ThrowIfOpen();
        var v = Math.Clamp(gain, _gainMin, _gainMax);
        if (NativeQhy.SetQHYCCDParam(_handle, (int)QhyControlId.CONTROL_GAIN, v) != QhyccdSuccess)
            throw new InvalidOperationException("SetQHYCCDParam(CONTROL_GAIN) failed.");
    }

    public void SetROI(int offsetX, int offsetY, int width, int height)
    {
        ThrowIfOpen();
        if (NativeQhy.SetQHYCCDResolution(_handle, (uint)offsetX, (uint)offsetY, (uint)width, (uint)height) !=
            QhyccdSuccess)
            throw new InvalidOperationException("SetQHYCCDResolution failed.");
        _width = width;
        _height = height;
        RefreshMemLength();
    }

    public void SetBinning(int binX, int binY)
    {
        ThrowIfOpen();
        if (binX != binY)
            throw new NotSupportedException("QHY binning uses a single factor for X and Y in this backend.");
        var b = (uint)Math.Clamp(binX, 1, _maxBin);
        if (NativeQhy.SetQHYCCDBinMode(_handle, b, b) != QhyccdSuccess)
            throw new InvalidOperationException("SetQHYCCDBinMode failed.");
        RefreshGeometryAfterBin();
        RefreshMemLength();
    }

    public bool SetPixelFormat(string format)
    {
        ThrowIfOpen();
        if (string.IsNullOrWhiteSpace(format))
            return false;
        var bits = format.Trim().ToUpperInvariant() switch
        {
            "MONO8" or "RAW8" or "BAYERRG8" => 8u,
            "MONO16" or "RAW16" or "BAYERRG16" => 16u,
            _ => 0u
        };
        if (bits == 0)
            return false;
        if (NativeQhy.SetQHYCCDBitsMode(_handle, bits) != QhyccdSuccess)
            return false;
        _bpp = bits;
        RefreshMemLength();
        return true;
    }

    public List<string> GetPixelFormatList() => new() { "MONO8", "MONO16", "RAW8", "RAW16" };

    public bool StartCapture(int timeoutMs = 5000)
    {
        ThrowIfOpen();
        lock (_gate)
        {
            StopCaptureInternal();
            if (NativeQhy.SetQHYCCDStreamMode(_handle, 1) != QhyccdSuccess)
                return false;
            if (NativeQhy.BeginQHYCCDLive(_handle) != QhyccdSuccess)
                return false;

            _captureRunning = true;
            _liveThread = new Thread(LiveLoop)
            {
                IsBackground = true,
                Name = "QHYLive"
            };
            _liveThread.Start();
        }

        return true;
    }

    public void StopCapture()
    {
        lock (_gate)
            StopCaptureInternal();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        lock (_gate)
        {
            StopCaptureInternal();
            CloseCameraInternal();
        }

        if (_ownsResourceSlice)
        {
            lock (s_qhyResourceLock)
            {
                s_qhyResourceRef = Math.Max(0, s_qhyResourceRef - 1);
                if (s_qhyResourceRef == 0)
                {
                    try
                    {
                        NativeQhy.ReleaseQHYCCDResource();
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }

            _ownsResourceSlice = false;
        }

        GC.SuppressFinalize(this);
    }

    private void LiveLoop()
    {
        var handle = _handle;
        if (handle == nint.Zero)
            return;

        var buffer = new byte[Math.Max(_memLength, (uint)(4 * 1024 * 1024))];
        while (_captureRunning)
        {
            uint w = 0, h = 0, bpp = 0, channels = 1;
            var rc = NativeQhy.GetQHYCCDLiveFrame(handle, ref w, ref h, ref bpp, ref channels, buffer);
            if (rc != QhyccdSuccess)
            {
                Thread.Sleep(2);
                continue;
            }

            var bytes = (int)(w * h * bpp / 8 * Math.Max(1, channels));
            bytes = Math.Min(bytes, buffer.Length);
            if (bytes <= 0)
                continue;

            var copy = new byte[bytes];
            Array.Copy(buffer, copy, bytes);
            _frameSeq++;
            var fmt = bpp >= 16 ? "MONO16" : (_isColor ? "BAYERRG8" : "MONO8");
            FrameArrived?.Invoke(this, new NativeFrameData
            {
                Data = copy,
                Width = (int)w,
                Height = (int)h,
                FrameId = _frameSeq,
                PixelFormatName = fmt
            });
        }
    }

    private void LoadCameraInfo()
    {
        double cw = 0, ch = 0;
        uint iw = 0, ih = 0;
        double pxw = 0, pxh = 0;
        uint bppChip = 0;
        if (NativeQhy.GetQHYCCDChipInfo(_handle, ref cw, ref ch, ref iw, ref ih, ref pxw, ref pxh, ref bppChip) ==
            QhyccdSuccess)
        {
            _width = (int)iw;
            _height = (int)ih;
            _pixelW = pxw;
            _pixelH = pxh;
            _bpp = bppChip > 0 ? bppChip : 8;
        }

        _isColor = NativeQhy.IsQHYCCDControlAvailable(_handle, (int)QhyControlId.CAM_IS_COLOR) == QhyccdSuccess;
        _bayerPattern = _isColor ? "BAYER_RG" : string.Empty;
        _modelName = _openedId;
        _sensorName = "QHY";
    }

    private void ApplyFullFrame()
    {
        if (NativeQhy.SetQHYCCDResolution(_handle, 0, 0, (uint)_width, (uint)_height) != QhyccdSuccess)
            throw new InvalidOperationException("SetQHYCCDResolution (full frame) failed.");
        RefreshMemLength();
    }

    private void RefreshGeometryAfterBin()
    {
        double cw = 0, ch = 0;
        uint iw = 0, ih = 0;
        double pxw = 0, pxh = 0;
        uint bpp = 0;
        if (NativeQhy.GetQHYCCDChipInfo(_handle, ref cw, ref ch, ref iw, ref ih, ref pxw, ref pxh, ref bpp) ==
            QhyccdSuccess)
        {
            _width = (int)iw;
            _height = (int)ih;
        }
    }

    private void DiscoverControlRanges()
    {
        _expMin = 1;
        _expMax = 3600_000;
        _gainMin = 0;
        _gainMax = 100;
        double min = 0, max = 0, step = 0;
        if (NativeQhy.GetQHYCCDParamMinMaxStep(_handle, (int)QhyControlId.CONTROL_EXPOSURE, ref min, ref max, ref step) ==
            QhyccdSuccess)
        {
            _expMin = min;
            _expMax = max;
        }

        if (NativeQhy.GetQHYCCDParamMinMaxStep(_handle, (int)QhyControlId.CONTROL_GAIN, ref min, ref max, ref step) ==
            QhyccdSuccess)
        {
            _gainMin = min;
            _gainMax = max;
        }

        _maxBin = 4;
    }

    private void RefreshMemLength()
    {
        var m = NativeQhy.GetQHYCCDMemLength(_handle);
        if (m > 0)
            _memLength = m;
        else
            _memLength = (uint)(_width * _height * Math.Max(1, (int)_bpp / 8));
    }

    private void CloseCameraInternal()
    {
        StopCaptureInternal();
        if (_handle != nint.Zero)
        {
            try
            {
                NativeQhy.CloseQHYCCD(_handle);
            }
            catch
            {
                // ignore
            }

            _handle = nint.Zero;
        }

        _openedId = string.Empty;
    }

    private void StopCaptureInternal()
    {
        _captureRunning = false;
        if (_liveThread is { IsAlive: true })
            _liveThread.Join(3000);
        _liveThread = null;
        if (_handle != nint.Zero)
        {
            try
            {
                NativeQhy.StopQHYCCDLive(_handle);
            }
            catch
            {
                // ignore
            }
        }
    }

    private void ThrowIfNoDll()
    {
        if (_dllMissing)
            throw new InvalidOperationException($"{DllName}.dll is not available.");
    }

    private void ThrowIfOpen()
    {
        ThrowIfNoDll();
        if (_handle == nint.Zero)
            throw new InvalidOperationException("Camera is not open.");
    }

    private static string NullTerminatedAscii(byte[] buf)
    {
        var len = Array.IndexOf(buf, (byte)0);
        if (len < 0)
            len = buf.Length;
        return Encoding.ASCII.GetString(buf, 0, len);
    }

    private static class NativeQhy
    {
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern uint InitQHYCCDResource();

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern uint ReleaseQHYCCDResource();

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern uint ScanQHYCCD();

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern uint GetQHYCCDId(uint index, [Out] byte[] id);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern uint GetQHYCCDModel(byte[] id, [Out] byte[] model);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern nint OpenQHYCCD(byte[] id);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern uint CloseQHYCCD(nint handle);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern uint InitQHYCCD(nint handle);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern uint SetQHYCCDStreamMode(nint handle, byte mode);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern uint SetQHYCCDBinMode(nint handle, uint wbin, uint hbin);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern uint SetQHYCCDResolution(nint handle, uint x, uint y, uint xsize, uint ysize);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern uint SetQHYCCDParam(nint handle, int controlId, double value);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern double GetQHYCCDParam(nint handle, int controlId);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern uint GetQHYCCDParamMinMaxStep(nint handle, int controlId, ref double min, ref double max,
            ref double step);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern uint SetQHYCCDBitsMode(nint handle, uint bits);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern uint BeginQHYCCDLive(nint handle);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern uint StopQHYCCDLive(nint handle);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern uint GetQHYCCDLiveFrame(nint handle, ref uint w, ref uint h, ref uint bpp, ref uint channels,
            [Out] byte[] rawArray);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern uint GetQHYCCDMemLength(nint handle);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern uint GetQHYCCDChipInfo(nint handle, ref double chipw, ref double chiph, ref uint imagew,
            ref uint imageh, ref double pixelw, ref double pixelh, ref uint bpp);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        public static extern uint IsQHYCCDControlAvailable(nint handle, int controlId);
    }
}
