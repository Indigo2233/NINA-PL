using System.Runtime.InteropServices;
using System.Text;

namespace NINA.PL.Equipment.Camera.Sdk;

/// <summary>
/// Native backend for Touptek Toupcam / Ogmacam-compatible DLLs (e.g. toupcam.dll, ogmacam.dll).
/// Windows SDK exports use <c>__stdcall</c> (<see cref="CallingConvention.StdCall"/>).
/// </summary>
public sealed class ToupcamBackend : INativeCameraBackend
{
    public const int ToupcamMax = 128;
    private const uint ToupcamEventImage = 0x0004;
    private const uint ToupcamOptionRaw = 0x04;
    private const uint ToupcamOptionBinning = 0x17;
    private const uint ToupcamOptionPixelFormat = 0x1a;

    private static string s_defaultDllName = "toupcam";

    /// <summary>Default module name without extension, used by the parameterless constructor.</summary>
    public static string DefaultDllModuleName
    {
        get => s_defaultDllName;
        set => s_defaultDllName = string.IsNullOrWhiteSpace(value) ? "toupcam" : value.Trim();
    }

    private readonly string _moduleName;
    private readonly string _vendorLabel;
    private nint _lib = nint.Zero;
    private ToupcamNative? _native;
    private bool _initialized;
    private bool _dllMissing;
    private nint _handle = nint.Zero;
    private string _openedCamId = string.Empty;
    private readonly object _gate = new();
    private Thread? _pullThread;
    private volatile bool _captureRunning;
    private readonly AutoResetEvent _frameSignal = new(false);
    private PTOUPCAM_EVENT_CALLBACK? _eventCb;
    private ulong _frameSeq;
    private int _sensorW;
    private int _sensorH;
    private int _roiW;
    private int _roiH;
    private uint _fourCc;
    private uint _rawBpp;
    private ushort _gainMin;
    private ushort _gainMax;
    private uint _expMin;
    private uint _expMax;
    private double _pixelSizeUm;
    private string _modelName = string.Empty;
    private string _sensorName = string.Empty;
    private bool _isColor;
    private string _bayerPattern = string.Empty;
    private int _maxBin = 8;
    private bool _disposed;

    public ToupcamBackend()
        : this(null, null)
    {
    }

    /// <param name="dllNameWithoutExtension">e.g. <c>ogmacam</c> for Daheng/Hikvision shims.</param>
    public ToupcamBackend(string? dllNameWithoutExtension)
        : this(dllNameWithoutExtension, null)
    {
    }

    private ToupcamBackend(string? dllNameWithoutExtension, string? vendorLabel)
    {
        var name = string.IsNullOrWhiteSpace(dllNameWithoutExtension) ? DefaultDllModuleName : dllNameWithoutExtension!.Trim();
        _moduleName = name;
        _vendorLabel = vendorLabel ?? (name.Equals("ogmacam", StringComparison.OrdinalIgnoreCase) ? "Ogmacam" : "Toupcam");
    }

    public event EventHandler<NativeFrameData>? FrameArrived;

    public bool IsConnected => _handle != nint.Zero;

    public int SensorWidth => _roiW;

    public int SensorHeight => _roiH;

    public double ExposureMin => _expMin;

    public double ExposureMax => _expMax;

    public double GainMin => _gainMin;

    public double GainMax => _gainMax;

    public int MaxBinX => _maxBin;

    public int MaxBinY => _maxBin;

    public double PixelSizeUm => _pixelSizeUm;

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
                if (!NativeLibrary.TryLoad(_moduleName, out _lib))
                {
                    _dllMissing = true;
                    return;
                }

                _native = ToupcamNative.Load(_lib);
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
        if (_dllMissing || _native is null)
            return new List<NativeCameraInfo>();

        var arr = new ToupcamDeviceV2[ToupcamMax];
        uint count;
        try
        {
            count = _native.EnumV2(arr);
        }
        catch (DllNotFoundException)
        {
            return new List<NativeCameraInfo>();
        }

        var list = new List<NativeCameraInfo>();
        for (var i = 0; i < count && i < ToupcamMax; i++)
        {
            var id = NullTrim(arr[i].id);
            if (string.IsNullOrEmpty(id))
                continue;

            var display = NullTrim(arr[i].displayname);
            list.Add(new NativeCameraInfo
            {
                SerialNumber = id,
                ModelName = string.IsNullOrEmpty(display) ? id : display,
                VendorName = _vendorLabel
            });
        }

        return list;
    }

    public void OpenCamera(string serialNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serialNumber);
        ThrowIfNoDll();
        lock (_gate)
        {
            CloseCameraInternal();
            _handle = _native!.Open(serialNumber);
            if (_handle == nint.Zero)
                throw new InvalidOperationException("Toupcam_Open failed.");

            _openedCamId = serialNumber;
            ReadStaticCameraInfo();
        }
    }

    public void CloseCamera()
    {
        lock (_gate)
            CloseCameraInternal();
    }

    public void SetExposureTime(double microseconds)
    {
        ThrowIfNotOpen();
        var us = (uint)Math.Clamp(microseconds, _expMin, _expMax);
        var hr = _native!.PutExpoTime(_handle, us);
        if (hr < 0)
            throw new InvalidOperationException($"Toupcam_put_ExpoTime failed (0x{hr:X8}).");
    }

    public double GetGain()
    {
        ThrowIfNotOpen();
        _native!.GetExpoAGain(_handle, out var g);
        return g;
    }

    public void SetGain(double gain)
    {
        ThrowIfNotOpen();
        var g = (ushort)Math.Clamp((int)Math.Round(gain), _gainMin, _gainMax);
        var hr = _native!.PutExpoAGain(_handle, g);
        if (hr < 0)
            throw new InvalidOperationException($"Toupcam_put_ExpoAGain failed (0x{hr:X8}).");
    }

    public void SetROI(int offsetX, int offsetY, int width, int height)
    {
        ThrowIfNotOpen();
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        var hr = _native!.PutRoi(_handle, (uint)offsetX, (uint)offsetY, (uint)width, (uint)height);
        if (hr < 0)
            throw new InvalidOperationException($"Toupcam_put_Roi failed (0x{hr:X8}).");

        RefreshGeometry();
    }

    public void SetBinning(int binX, int binY)
    {
        ThrowIfNotOpen();
        if (binX != binY)
            throw new NotSupportedException("Toupcam digital binning uses a single factor for X and Y.");

        var n = Math.Clamp(binX, 1, _maxBin);
        var hr = _native!.PutOption(_handle, ToupcamOptionBinning, n);
        if (hr < 0)
            throw new InvalidOperationException($"Toupcam_put_Option(BINNING) failed (0x{hr:X8}).");
        RefreshGeometry();
    }

    public bool SetPixelFormat(string format)
    {
        ThrowIfNotOpen();
        if (!TryMapPixelFormat(format, out var opt))
            return false;
        var hr = _native!.PutOption(_handle, ToupcamOptionPixelFormat, opt);
        if (hr < 0)
            return false;
        _native.GetRawFormat(_handle, out _fourCc, out _rawBpp);
        return true;
    }

    public List<string> GetPixelFormatList() =>
        new() { "RAW8", "RAW10", "RAW12", "RAW14", "RAW16", "RGB888", "MONO8", "MONO16" };

    public bool StartCapture(int timeoutMs = 5000)
    {
        ThrowIfNotOpen();
        lock (_gate)
        {
            StopCaptureInternal();
            _eventCb = OnToupcamEvent;
            var hr = _native!.StartPullModeWithCallback(_handle, _eventCb, nint.Zero);
            if (hr < 0)
                return false;

            _captureRunning = true;
            _pullThread = new Thread(PullLoop)
            {
                IsBackground = true,
                Name = "ToupcamPull"
            };
            _pullThread.Start();
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
            if (_lib != nint.Zero)
            {
                try
                {
                    NativeLibrary.Free(_lib);
                }
                catch
                {
                    // ignore
                }

                _lib = nint.Zero;
            }

            _native = null;
        }

        _frameSignal.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnToupcamEvent(uint evt, nint ctx)
    {
        if (evt == ToupcamEventImage)
            _frameSignal.Set();
    }

    private unsafe void PullLoop()
    {
        var native = _native;
        var handle = _handle;
        if (native is null || handle == nint.Zero)
            return;

        var buffer = Array.Empty<byte>();
        while (_captureRunning)
        {
            if (!_frameSignal.WaitOne(100))
                continue;

            var localHandle = nint.Zero;
            int w, h, bpp, rowPitch;
            lock (_gate)
            {
                localHandle = _handle;
                if (!_captureRunning || localHandle == nint.Zero || _native is null)
                    break;
                native = _native;
                native.GetSize(localHandle, out w, out h);
                bpp = (int)_rawBpp;
                if (bpp <= 0)
                    bpp = 8;
                rowPitch = (w * bpp + 7) / 8;
                var need = rowPitch * h;
                if (buffer.Length < need)
                    buffer = new byte[need];
            }

            ToupcamFrameInfoV3 info;
            unsafe
            {
                fixed (byte* p = buffer)
                {
                    var hr = native!.PullImageV3(localHandle, (nint)p, 0, 0, 0, out info);
                    if (hr < 0)
                        continue;
                }
            }

            var bytesPerPixel = Math.Max(1, (bpp + 7) / 8);
            var copyLen = (int)(info.width * info.height * (uint)bytesPerPixel);
            copyLen = Math.Min(copyLen, buffer.Length);
            if (copyLen <= 0)
                continue;

            var data = new byte[copyLen];
            Array.Copy(buffer, data, copyLen);
            _frameSeq++;
            FrameArrived?.Invoke(this, new NativeFrameData
            {
                Data = data,
                Width = (int)info.width,
                Height = (int)info.height,
                FrameId = _frameSeq,
                PixelFormatName = DescribePixelFormat()
            });
        }
    }

    private string DescribePixelFormat()
    {
        if (_isColor && _fourCc != 0)
            return FourCcToBayerName(_fourCc);
        return _rawBpp >= 16 ? "MONO16" : "MONO8";
    }

    private static string FourCcToBayerName(uint fourCc)
    {
        var b = BitConverter.GetBytes(fourCc);
        if (b.Length < 4)
            return "BAYERRG8";
        var s = $"{(char)b[0]}{(char)b[1]}{(char)b[2]}{(char)b[3]}";
        return "BAYER_" + s.Trim('\0').ToUpperInvariant();
    }

    private unsafe void ReadStaticCameraInfo()
    {
        _native!.GetSize(_handle, out _sensorW, out _sensorH);
        _roiW = _sensorW;
        _roiH = _sensorH;

        _native.GetExpTimeRange(_handle, out _expMin, out _expMax, out _);
        _native.GetExpoAGainRange(_handle, out _gainMin, out _gainMax, out _);

        _native.GetRawFormat(_handle, out _fourCc, out _rawBpp);
        _native.GetChrome(_handle, out var chrome);
        _isColor = chrome == 0;

        Span<byte> sn = stackalloc byte[32];
        unsafe
        {
            fixed (byte* p = sn)
                _native.GetSerialNumber(_handle, (nint)p);
        }

        _sensorName = Encoding.ASCII.GetString(sn).TrimEnd('\0');
        if (string.IsNullOrEmpty(_sensorName))
            _sensorName = "Toupcam";

        _modelName = NullTrim(_openedCamId);
        _bayerPattern = _isColor ? FourCcToBayerName(_fourCc) : string.Empty;

        _native.PutOption(_handle, ToupcamOptionRaw, 1);

        if (_native.GetPixelSize(_handle, 0, out var px, out var py) >= 0)
            _pixelSizeUm = px > 0 ? px : py;
        else
            _pixelSizeUm = 0;
    }

    private void RefreshGeometry()
    {
        if (_handle == nint.Zero || _native is null)
            return;
        _native.GetSize(_handle, out _roiW, out _roiH);
    }

    private void CloseCameraInternal()
    {
        StopCaptureInternal();
        if (_handle != nint.Zero && _native is not null)
        {
            try
            {
                _native.Close(_handle);
            }
            catch
            {
                // ignore
            }

            _handle = nint.Zero;
        }

        _openedCamId = string.Empty;
    }

    private void StopCaptureInternal()
    {
        _captureRunning = false;
        _frameSignal.Set();
        if (_pullThread is { IsAlive: true })
            _pullThread.Join(3000);

        _pullThread = null;
        if (_handle != nint.Zero && _native is not null)
        {
            try
            {
                _native.Stop(_handle);
            }
            catch
            {
                // ignore
            }
        }

        _eventCb = null;
    }

    private void ThrowIfNoDll()
    {
        if (_dllMissing || _native is null)
            throw new InvalidOperationException($"Native module '{_moduleName}' is not available.");
    }

    private void ThrowIfNotOpen()
    {
        ThrowIfNoDll();
        if (_handle == nint.Zero)
            throw new InvalidOperationException("Camera is not open.");
    }

    private static bool TryMapPixelFormat(string format, out int opt)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            opt = -1;
            return false;
        }

        opt = format.Trim().ToUpperInvariant() switch
        {
            "RAW8" => 0x00,
            "RAW10" => 0x01,
            "RAW12" => 0x02,
            "RAW14" => 0x03,
            "RAW16" => 0x04,
            "RGB888" or "RGB24" => 0x08,
            "MONO8" => 0x00,
            "MONO16" => 0x04,
            _ => -1
        };
        return opt >= 0;
    }

    private static string NullTrim(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;
        var i = s.IndexOf('\0');
        return i >= 0 ? s[..i] : s;
    }

    /// <summary>Matches Toupcam <c>ToupcamDeviceV2</c> / legacy <c>Toupcam_InstV2</c> layout (Unicode).</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct ToupcamDeviceV2
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string displayname;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string id;

        public nint model;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ToupcamFrameInfoV3
    {
        public uint width;
        public uint height;
        public uint flag;
        public uint seq;
        public ulong timestamp;
        public uint shutterseq;
        public uint expotime;
        public ushort expogain;
        public ushort blacklevel;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void PTOUPCAM_EVENT_CALLBACK(uint nEvent, nint ctxEvent);

    private sealed class ToupcamNative
    {
        private readonly Toupcam_EnumV2 _enumV2;
        private readonly Toupcam_Open _open;
        private readonly Toupcam_Close _close;
        private readonly Toupcam_StartPullModeWithCallback _startPullCb;
        private readonly Toupcam_Stop _stop;
        private readonly Toupcam_PullImageV3 _pullV3;
        private readonly Toupcam_put_ExpoTime _putExpoTime;
        private readonly Toupcam_get_ExpoAGain _getExpoAGain;
        private readonly Toupcam_put_ExpoAGain _putExpoAGain;
        private readonly Toupcam_put_Roi _putRoi;
        private readonly Toupcam_get_Size _getSize;
        private readonly Toupcam_get_RawFormat _getRawFormat;
        private readonly Toupcam_put_Option _putOption;
        private readonly Toupcam_get_ExpTimeRange _getExpTimeRange;
        private readonly Toupcam_get_ExpoAGainRange _getExpoAGainRange;
        private readonly Toupcam_get_Chrome _getChrome;
        private readonly Toupcam_get_SerialNumber _getSerialNumber;
        private readonly Toupcam_get_PixelSize? _getPixelSize;

        private ToupcamNative(
            Toupcam_EnumV2 enumV2,
            Toupcam_Open open,
            Toupcam_Close close,
            Toupcam_StartPullModeWithCallback startPullCb,
            Toupcam_Stop stop,
            Toupcam_PullImageV3 pullV3,
            Toupcam_put_ExpoTime putExpoTime,
            Toupcam_get_ExpoAGain getExpoAGain,
            Toupcam_put_ExpoAGain putExpoAGain,
            Toupcam_put_Roi putRoi,
            Toupcam_get_Size getSize,
            Toupcam_get_RawFormat getRawFormat,
            Toupcam_put_Option putOption,
            Toupcam_get_ExpTimeRange getExpTimeRange,
            Toupcam_get_ExpoAGainRange getExpoAGainRange,
            Toupcam_get_Chrome getChrome,
            Toupcam_get_SerialNumber getSerialNumber,
            Toupcam_get_PixelSize? getPixelSize)
        {
            _enumV2 = enumV2;
            _open = open;
            _close = close;
            _startPullCb = startPullCb;
            _stop = stop;
            _pullV3 = pullV3;
            _putExpoTime = putExpoTime;
            _getExpoAGain = getExpoAGain;
            _putExpoAGain = putExpoAGain;
            _putRoi = putRoi;
            _getSize = getSize;
            _getRawFormat = getRawFormat;
            _putOption = putOption;
            _getExpTimeRange = getExpTimeRange;
            _getExpoAGainRange = getExpoAGainRange;
            _getChrome = getChrome;
            _getSerialNumber = getSerialNumber;
            _getPixelSize = getPixelSize;
        }

        public static ToupcamNative Load(nint lib)
        {
            T G<T>(string name) where T : class =>
                Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(lib, name));

            Toupcam_get_PixelSize? pix = null;
            try
            {
                pix = G<Toupcam_get_PixelSize>("Toupcam_get_PixelSize");
            }
            catch (EntryPointNotFoundException)
            {
                // older DLLs
            }

            return new ToupcamNative(
                G<Toupcam_EnumV2>("Toupcam_EnumV2"),
                G<Toupcam_Open>("Toupcam_Open"),
                G<Toupcam_Close>("Toupcam_Close"),
                G<Toupcam_StartPullModeWithCallback>("Toupcam_StartPullModeWithCallback"),
                G<Toupcam_Stop>("Toupcam_Stop"),
                G<Toupcam_PullImageV3>("Toupcam_PullImageV3"),
                G<Toupcam_put_ExpoTime>("Toupcam_put_ExpoTime"),
                G<Toupcam_get_ExpoAGain>("Toupcam_get_ExpoAGain"),
                G<Toupcam_put_ExpoAGain>("Toupcam_put_ExpoAGain"),
                G<Toupcam_put_Roi>("Toupcam_put_Roi"),
                G<Toupcam_get_Size>("Toupcam_get_Size"),
                G<Toupcam_get_RawFormat>("Toupcam_get_RawFormat"),
                G<Toupcam_put_Option>("Toupcam_put_Option"),
                G<Toupcam_get_ExpTimeRange>("Toupcam_get_ExpTimeRange"),
                G<Toupcam_get_ExpoAGainRange>("Toupcam_get_ExpoAGainRange"),
                G<Toupcam_get_Chrome>("Toupcam_get_Chrome"),
                G<Toupcam_get_SerialNumber>("Toupcam_get_SerialNumber"),
                pix);
        }

        public uint EnumV2(ToupcamDeviceV2[] arr) => _enumV2(arr);

        public unsafe nint Open(string camId)
        {
            fixed (char* p = camId)
                return _open((nint)p);
        }

        public void Close(nint h) => _close(h);

        public int StartPullModeWithCallback(nint h, PTOUPCAM_EVENT_CALLBACK cb, nint ctx) => _startPullCb(h, cb, ctx);

        public int Stop(nint h) => _stop(h);

        public int PullImageV3(nint h, nint buf, int still, int bits, int pitch, out ToupcamFrameInfoV3 info) =>
            _pullV3(h, buf, still, bits, pitch, out info);

        public int PutExpoTime(nint h, uint us) => _putExpoTime(h, us);
        public int GetExpoAGain(nint h, out ushort g) => _getExpoAGain(h, out g);
        public int PutExpoAGain(nint h, ushort g) => _putExpoAGain(h, g);
        public int PutRoi(nint h, uint x, uint y, uint w, uint ht) => _putRoi(h, x, y, w, ht);
        public int GetSize(nint h, out int w, out int ht) => _getSize(h, out w, out ht);
        public int GetRawFormat(nint h, out uint fourcc, out uint bpp) => _getRawFormat(h, out fourcc, out bpp);
        public int PutOption(nint h, uint opt, int val) => _putOption(h, opt, val);
        public int GetExpTimeRange(nint h, out uint min, out uint max, out uint def) => _getExpTimeRange(h, out min, out max, out def);
        public int GetExpoAGainRange(nint h, out ushort min, out ushort max, out ushort def) => _getExpoAGainRange(h, out min, out max, out def);
        public int GetChrome(nint h, out int chrome) => _getChrome(h, out chrome);
        public int GetSerialNumber(nint h, nint sn) => _getSerialNumber(h, sn);

        public int GetPixelSize(nint h, uint resIndex, out float x, out float y)
        {
            if (_getPixelSize is null)
            {
                x = 0;
                y = 0;
                return -1;
            }

            return _getPixelSize(h, resIndex, out x, out y);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint Toupcam_EnumV2([Out][MarshalAs(UnmanagedType.LPArray, SizeConst = ToupcamMax)] ToupcamDeviceV2[] arr);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate nint Toupcam_Open(nint camId);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void Toupcam_Close(nint h);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int Toupcam_StartPullModeWithCallback(nint h, PTOUPCAM_EVENT_CALLBACK cb, nint ctx);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int Toupcam_Stop(nint h);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int Toupcam_PullImageV3(nint h, nint pImageData, int bStill, int bits, int rowPitch, out ToupcamFrameInfoV3 pInfo);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int Toupcam_put_ExpoTime(nint h, uint time);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int Toupcam_get_ExpoAGain(nint h, out ushort gain);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int Toupcam_put_ExpoAGain(nint h, ushort gain);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int Toupcam_put_Roi(nint h, uint x, uint y, uint w, uint ht);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int Toupcam_get_Size(nint h, out int w, out int ht);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int Toupcam_get_RawFormat(nint h, out uint fourCC, out uint bpp);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int Toupcam_put_Option(nint h, uint option, int value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int Toupcam_get_ExpTimeRange(nint h, out uint min, out uint max, out uint def);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int Toupcam_get_ExpoAGainRange(nint h, out ushort min, out ushort max, out ushort def);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int Toupcam_get_Chrome(nint h, out int bChrome);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int Toupcam_get_SerialNumber(nint h, nint sn);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int Toupcam_get_PixelSize(nint h, uint nResolutionIndex, out float x, out float y);
    }
}
