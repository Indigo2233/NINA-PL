using System.Runtime.InteropServices;
using System.Text;

namespace NINA.PL.Equipment.Camera.Sdk;

/// <summary>Player One Astronomy cameras via <c>PlayerOneCamera.dll</c> (video exposure + <c>POAGetImageData</c> polling).</summary>
public sealed class PlayerOneBackend : INativeCameraBackend
{
    private const string DllName = "PlayerOneCamera";

    private bool _initialized;
    private bool _dllMissing;
    private int _cameraId = -1;
    private readonly object _gate = new();
    private Thread? _videoThread;
    private volatile bool _captureRunning;
    private ulong _frameSeq;
    private int _width;
    private int _height;
    private int _bin = 1;
    private POAImgFormat _imgFormat = POAImgFormat.POA_RAW8;
    private long _expMin;
    private long _expMax;
    private long _gainMin;
    private long _gainMax;
    private int _maxBin = 4;
    private string _modelName = string.Empty;
    private string _sensorName = string.Empty;
    private string _serial = string.Empty;
    private bool _isColor;
    private string _bayerPattern = string.Empty;
    private double _pixelSizeUm;
    private bool _disposed;

    public event EventHandler<NativeFrameData>? FrameArrived;

    public bool IsConnected => _cameraId >= 0;

    public int SensorWidth => _width;

    public int SensorHeight => _height;

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
                _ = NativePoa.POAGetCameraCount();
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
            var n = NativePoa.POAGetCameraCount();
            var list = new List<NativeCameraInfo>();
            for (var i = 0; i < n; i++)
            {
                var prop = NewCameraProperties();
                if (NativePoa.POAGetCameraProperties(i, ref prop) != POAErrors.POA_OK)
                    continue;

                var sn = NullTrim(prop.SN);
                if (string.IsNullOrEmpty(sn))
                    sn = prop.cameraID.ToString();

                var name = NullTrim(prop.cameraModelName);
                list.Add(new NativeCameraInfo
                {
                    SerialNumber = sn,
                    ModelName = string.IsNullOrEmpty(name) ? sn : name,
                    VendorName = "Player One"
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

        lock (_gate)
        {
            CloseCameraInternal();
            var n = NativePoa.POAGetCameraCount();
            POACameraProperties found = default;
            var ok = false;
            for (var i = 0; i < n; i++)
            {
                var prop = NewCameraProperties();
                if (NativePoa.POAGetCameraProperties(i, ref prop) != POAErrors.POA_OK)
                    continue;
                var sn = NullTrim(prop.SN);
                if (string.IsNullOrEmpty(sn))
                    sn = prop.cameraID.ToString();
                if (!string.Equals(sn, serialNumber, StringComparison.Ordinal))
                    continue;
                found = prop;
                ok = true;
                break;
            }

            if (!ok)
                throw new InvalidOperationException("No Player One camera matches the requested serial.");

            if (NativePoa.POAOpenCamera(found.cameraID) != POAErrors.POA_OK)
                throw new InvalidOperationException("POAOpenCamera failed.");
            if (NativePoa.POAInitCamera(found.cameraID) != POAErrors.POA_OK)
            {
                NativePoa.POACloseCamera(found.cameraID);
                throw new InvalidOperationException("POAInitCamera failed.");
            }

            _cameraId = found.cameraID;
            _serial = NullTrim(found.SN);
            _modelName = NullTrim(found.cameraModelName);
            _sensorName = NullTrim(found.sensorModelName);
            if (string.IsNullOrEmpty(_sensorName))
                _sensorName = _modelName;
            _isColor = found.isColorCamera != POABool.POA_FALSE;
            _bayerPattern = MapBayer(found.bayerPattern);
            _pixelSizeUm = found.pixelSize;
            DiscoverBinsAndConfigRanges(found);
            ApplyDefaultFormatAndSize(found);
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
        var v = new POAConfigValue { intValue = (long)Math.Clamp(microseconds, _expMin, _expMax) };
        if (NativePoa.POASetConfig(_cameraId, POAConfig.POA_EXPOSURE, v, POABool.POA_FALSE) != POAErrors.POA_OK)
            throw new InvalidOperationException("POASetConfig(POA_EXPOSURE) failed.");
    }

    public double GetGain()
    {
        ThrowIfOpen();
        var val = new POAConfigValue();
        var auto = POABool.POA_FALSE;
        if (NativePoa.POAGetConfig(_cameraId, POAConfig.POA_GAIN, ref val, ref auto) != POAErrors.POA_OK)
            return 0;
        return val.intValue;
    }

    public void SetGain(double gain)
    {
        ThrowIfOpen();
        var g = (long)Math.Clamp((long)Math.Round(gain), _gainMin, _gainMax);
        var v = new POAConfigValue { intValue = g };
        if (NativePoa.POASetConfig(_cameraId, POAConfig.POA_GAIN, v, POABool.POA_FALSE) != POAErrors.POA_OK)
            throw new InvalidOperationException("POASetConfig(POA_GAIN) failed.");
    }

    public void SetROI(int offsetX, int offsetY, int width, int height)
    {
        ThrowIfOpen();
        if (NativePoa.POASetImageStartPos(_cameraId, offsetX, offsetY) != POAErrors.POA_OK)
            throw new InvalidOperationException("POASetImageStartPos failed.");
        if (NativePoa.POASetImageSize(_cameraId, width, height) != POAErrors.POA_OK)
            throw new InvalidOperationException("POASetImageSize failed.");
        RefreshImageGeometry();
    }

    public void SetBinning(int binX, int binY)
    {
        ThrowIfOpen();
        if (binX != binY)
            throw new NotSupportedException("Player One SDK uses a single bin factor.");
        var b = Math.Clamp(binX, 1, _maxBin);
        if (NativePoa.POASetImageBin(_cameraId, b) != POAErrors.POA_OK)
            throw new InvalidOperationException("POASetImageBin failed.");
        _bin = b;
        RefreshImageGeometry();
    }

    public bool SetPixelFormat(string format)
    {
        ThrowIfOpen();
        if (!TryMapFormat(format, out var f))
            return false;
        if (NativePoa.POASetImageFormat(_cameraId, f) != POAErrors.POA_OK)
            return false;
        _imgFormat = f;
        return true;
    }

    public List<string> GetPixelFormatList() => new() { "RAW8", "RAW16", "RGB24", "MONO8" };

    public bool StartCapture(int timeoutMs = 5000)
    {
        ThrowIfOpen();
        lock (_gate)
        {
            StopCaptureInternal();
            if (NativePoa.POAStartExposure(_cameraId, POABool.POA_FALSE) != POAErrors.POA_OK)
                return false;

            _captureRunning = true;
            _videoThread = new Thread(VideoLoop)
            {
                IsBackground = true,
                Name = "PlayerOneVideo"
            };
            _videoThread.Start();
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

        GC.SuppressFinalize(this);
    }

    private void VideoLoop()
    {
        var id = _cameraId;
        if (id < 0)
            return;

        var bufSize = BufferSizeForFormat();
        var buffer = new byte[bufSize];
        while (_captureRunning)
        {
            var ready = POABool.POA_FALSE;
            if (NativePoa.POAImageReady(id, ref ready) == POAErrors.POA_OK && ready == POABool.POA_FALSE)
            {
                Thread.Sleep(2);
                continue;
            }

            var waitMs = 500;
            if (NativePoa.POAGetImageData(id, buffer, bufSize, waitMs) != POAErrors.POA_OK)
                continue;

            _frameSeq++;
            var copy = new byte[bufSize];
            Array.Copy(buffer, copy, bufSize);
            var fmt = _imgFormat switch
            {
                POAImgFormat.POA_RAW16 => "MONO16",
                POAImgFormat.POA_RGB24 => "RGB24",
                POAImgFormat.POA_MONO8 => "MONO8",
                _ => _isColor ? "BAYERRG8" : "MONO8"
            };

            FrameArrived?.Invoke(this, new NativeFrameData
            {
                Data = copy,
                Width = _width,
                Height = _height,
                FrameId = _frameSeq,
                PixelFormatName = fmt
            });
        }
    }

    private int BufferSizeForFormat() =>
        _imgFormat switch
        {
            POAImgFormat.POA_RAW16 => _width * _height * 2,
            POAImgFormat.POA_RGB24 => _width * _height * 3,
            POAImgFormat.POA_MONO8 => _width * _height,
            _ => _width * _height
        };

    private void DiscoverBinsAndConfigRanges(in POACameraProperties prop)
    {
        _expMin = 1;
        _expMax = 3600_000;
        _gainMin = 0;
        _gainMax = 100;
        _maxBin = 1;
        if (prop.bins is not null)
        {
            foreach (var b in prop.bins)
            {
                if (b <= 0)
                    break;
                _maxBin = Math.Max(_maxBin, b);
            }
        }

        var attr = NewConfigAttributes();
        if (NativePoa.POAGetConfigAttributesByConfigID(_cameraId, POAConfig.POA_EXPOSURE, ref attr) == POAErrors.POA_OK)
        {
            _expMin = attr.minValue.intValue;
            _expMax = attr.maxValue.intValue;
        }

        if (NativePoa.POAGetConfigAttributesByConfigID(_cameraId, POAConfig.POA_GAIN, ref attr) == POAErrors.POA_OK)
        {
            _gainMin = attr.minValue.intValue;
            _gainMax = attr.maxValue.intValue;
        }
    }

    private void ApplyDefaultFormatAndSize(in POACameraProperties prop)
    {
        _imgFormat = POAImgFormat.POA_RAW8;
        for (var i = 0; i < 8; i++)
        {
            var f = (POAImgFormat)prop.imgFormats[i];
            if (f == POAImgFormat.POA_END)
                break;
            if (f == POAImgFormat.POA_RAW8 || f == POAImgFormat.POA_RAW16)
            {
                _imgFormat = f;
                break;
            }
        }

        if (NativePoa.POASetImageFormat(_cameraId, _imgFormat) != POAErrors.POA_OK)
            throw new InvalidOperationException("POASetImageFormat failed.");
        if (NativePoa.POASetImageBin(_cameraId, 1) != POAErrors.POA_OK)
            throw new InvalidOperationException("POASetImageBin failed.");
        if (NativePoa.POASetImageStartPos(_cameraId, 0, 0) != POAErrors.POA_OK)
            throw new InvalidOperationException("POASetImageStartPos failed.");
        if (NativePoa.POASetImageSize(_cameraId, prop.maxWidth, prop.maxHeight) != POAErrors.POA_OK)
            throw new InvalidOperationException("POASetImageSize failed.");
        RefreshImageGeometry();
    }

    private void RefreshImageGeometry()
    {
        if (NativePoa.POAGetImageSize(_cameraId, out var w, out var h) != POAErrors.POA_OK)
            return;
        _width = w;
        _height = h;
        _ = NativePoa.POAGetImageBin(_cameraId, out _bin);
    }

    private void CloseCameraInternal()
    {
        StopCaptureInternal();
        if (_cameraId >= 0)
        {
            try
            {
                NativePoa.POACloseCamera(_cameraId);
            }
            catch
            {
                // ignore
            }

            _cameraId = -1;
        }

        _serial = string.Empty;
    }

    private void StopCaptureInternal()
    {
        _captureRunning = false;
        if (_videoThread is { IsAlive: true })
            _videoThread.Join(3000);
        _videoThread = null;
        if (_cameraId >= 0)
        {
            try
            {
                NativePoa.POAStopExposure(_cameraId);
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
        if (_cameraId < 0)
            throw new InvalidOperationException("Camera is not open.");
    }

    private static POACameraProperties NewCameraProperties() =>
        new()
        {
            cameraModelName = new string('\0', 255),
            userCustomID = new string('\0', 15),
            SN = new string('\0', 63),
            sensorModelName = new string('\0', 31),
            localPath = new string('\0', 255),
            bins = new int[8],
            imgFormats = new int[8],
            reserved = new byte[248]
        };

    private static POAConfigAttributes NewConfigAttributes() =>
        new()
        {
            szConfName = new string('\0', 63),
            szDescription = new string('\0', 127),
            reserved = new byte[64]
        };

    private static string NullTrim(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;
        var i = s.IndexOf('\0');
        return i >= 0 ? s[..i] : s;
    }

    private static string MapBayer(POABayerPattern p) =>
        p switch
        {
            POABayerPattern.POA_BAYER_RG => "BAYER_RG",
            POABayerPattern.POA_BAYER_BG => "BAYER_BG",
            POABayerPattern.POA_BAYER_GR => "BAYER_GR",
            POABayerPattern.POA_BAYER_GB => "BAYER_GB",
            _ => string.Empty
        };

    private static bool TryMapFormat(string format, out POAImgFormat f)
    {
        f = POAImgFormat.POA_RAW8;
        if (string.IsNullOrWhiteSpace(format))
            return false;
        f = format.Trim().ToUpperInvariant() switch
        {
            "RAW8" or "MONO8" or "BAYERRG8" => POAImgFormat.POA_RAW8,
            "RAW16" or "MONO16" or "BAYERRG16" => POAImgFormat.POA_RAW16,
            "RGB24" or "RGB8" => POAImgFormat.POA_RGB24,
            _ => POAImgFormat.POA_END
        };
        return f != POAImgFormat.POA_END;
    }

    private enum POAErrors
    {
        POA_OK = 0,
        POA_ERROR_INVALID_INDEX,
        POA_ERROR_INVALID_ID,
        POA_ERROR_INVALID_CONFIG,
        POA_ERROR_INVALID_ARGU,
        POA_ERROR_NOT_OPENED,
        POA_ERROR_DEVICE_NOT_FOUND,
        POA_ERROR_OUT_OF_LIMIT,
        POA_ERROR_EXPOSURE_FAILED,
        POA_ERROR_TIMEOUT,
        POA_ERROR_SIZE_LESS,
        POA_ERROR_EXPOSING,
        POA_ERROR_POINTER,
        POA_ERROR_CONF_CANNOT_WRITE,
        POA_ERROR_CONF_CANNOT_READ,
        POA_ERROR_ACCESS_DENIED,
        POA_ERROR_OPERATION_FAILED,
        POA_ERROR_MEMORY_FAILED
    }

    private enum POABool
    {
        POA_FALSE,
        POA_TRUE
    }

    private enum POABayerPattern
    {
        POA_BAYER_RG = 0,
        POA_BAYER_BG,
        POA_BAYER_GR,
        POA_BAYER_GB,
        POA_BAYER_MONO = -1
    }

    private enum POAImgFormat
    {
        POA_RAW8 = 0,
        POA_RAW16 = 1,
        POA_RGB24 = 2,
        POA_MONO8 = 3,
        POA_END = -1
    }

    private enum POAConfig
    {
        POA_EXPOSURE = 0,
        POA_GAIN
    }

    [StructLayout(LayoutKind.Explicit, Size = 8)]
    private struct POAConfigValue
    {
        [FieldOffset(0)]
        public long intValue;

        [FieldOffset(0)]
        public double floatValue;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct POAConfigAttributes
    {
        public POABool isSupportAuto;
        public POABool isWritable;
        public POABool isReadable;
        public POAConfig configID;
        public POAValueType valueType;
        public POAConfigValue maxValue;
        public POAConfigValue minValue;
        public POAConfigValue defaultValue;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szConfName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szDescription;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[]? reserved;
    }

    private enum POAValueType
    {
        VAL_INT = 0,
        VAL_FLOAT,
        VAL_BOOL
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct POACameraProperties
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string cameraModelName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
        public string userCustomID;

        public int cameraID;
        public int maxWidth;
        public int maxHeight;
        public int bitDepth;
        public POABool isColorCamera;
        public POABool isHasST4Port;
        public POABool isHasCooler;
        public POABool isUSB3Speed;
        public POABayerPattern bayerPattern;
        public double pixelSize;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string SN;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string sensorModelName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string localPath;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public int[] bins;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public int[] imgFormats;

        public POABool isSupportHardBin;
        public int pID;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 248)]
        public byte[]? reserved;
    }

    private static class NativePoa
    {
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int POAGetCameraCount();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern POAErrors POAGetCameraProperties(int nIndex, ref POACameraProperties pProp);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern POAErrors POAOpenCamera(int nCameraID);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern POAErrors POAInitCamera(int nCameraID);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern POAErrors POACloseCamera(int nCameraID);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern POAErrors POASetImageSize(int nCameraID, int width, int height);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern POAErrors POAGetImageSize(int nCameraID, out int pWidth, out int pHeight);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern POAErrors POASetImageStartPos(int nCameraID, int startX, int startY);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern POAErrors POASetImageBin(int nCameraID, int bin);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern POAErrors POAGetImageBin(int nCameraID, out int pBin);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern POAErrors POASetImageFormat(int nCameraID, POAImgFormat imgFormat);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern POAErrors POASetConfig(int nCameraID, POAConfig confID, POAConfigValue confValue,
            POABool isAuto);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern POAErrors POAGetConfig(int nCameraID, POAConfig confID, ref POAConfigValue pConfValue,
            ref POABool pIsAuto);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern POAErrors POAGetConfigAttributesByConfigID(int nCameraID, POAConfig confID,
            ref POAConfigAttributes pConfAttr);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern POAErrors POAStartExposure(int nCameraID, POABool bSingleFrame);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern POAErrors POAStopExposure(int nCameraID);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern POAErrors POAImageReady(int nCameraID, ref POABool pIsReady);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern POAErrors POAGetImageData(int nCameraID, [Out] byte[] pBuf, int lBufSize, int nTimeoutms);
    }
}
