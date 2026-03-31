using System.Globalization;
using System.Runtime.InteropServices;

namespace NINA.PL.Equipment.Camera.Sdk;

/// <summary>ZWO ASI cameras via <c>ASICamera2.dll</c> (polling <c>ASIGetVideoData</c> on a worker thread).</summary>
public sealed class ZwoAsiBackend : INativeCameraBackend
{
    private const string DllName = "ASICamera2";

    private bool _initialized;
    private bool _dllMissing;
    private int _cameraId = -1;
    private readonly object _gate = new();
    private Thread? _videoThread;
    private volatile bool _captureRunning;
    private ulong _frameSeq;
    private int _width;
    private int _height;
    private int _bin;
    private ASI_IMG_TYPE _imgType = ASI_IMG_TYPE.ASI_IMG_RAW8;
    private long _gainMin;
    private long _gainMax;
    private long _expMinUs;
    private long _expMaxUs;
    private string _modelName = string.Empty;
    private string _sensorName = string.Empty;
    private bool _isColor;
    private string _bayerPattern = string.Empty;
    private double _pixelSizeUm;
    private int _maxBin = 1;
    private bool _disposed;

    public event EventHandler<NativeFrameData>? FrameArrived;

    public bool IsConnected => _cameraId >= 0;

    public int SensorWidth => _width;

    public int SensorHeight => _height;

    public double ExposureMin => _expMinUs;

    public double ExposureMax => _expMaxUs;

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
                _ = NativeAsi.ASIGetNumOfConnectedCameras();
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
            var n = NativeAsi.ASIGetNumOfConnectedCameras();
            var list = new List<NativeCameraInfo>();
            for (var i = 0; i < n; i++)
            {
                var info = new ASI_CAMERA_INFO();
                if (NativeAsi.ASIGetCameraProperty(ref info, i) != ASI_ERROR_CODE.ASI_SUCCESS)
                    continue;

                list.Add(new NativeCameraInfo
                {
                    SerialNumber = info.CameraID.ToString(CultureInfo.InvariantCulture),
                    ModelName = NullTrim(info.Name),
                    VendorName = "ZWO"
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
        if (!int.TryParse(serialNumber, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            throw new ArgumentException("ASI device id must be the numeric CameraID from enumeration.", nameof(serialNumber));

        lock (_gate)
        {
            CloseCameraInternal();
            if (NativeAsi.ASIOpenCamera(id) != ASI_ERROR_CODE.ASI_SUCCESS)
                throw new InvalidOperationException("ASIOpenCamera failed.");
            if (NativeAsi.ASIInitCamera(id) != ASI_ERROR_CODE.ASI_SUCCESS)
            {
                NativeAsi.ASICloseCamera(id);
                throw new InvalidOperationException("ASIInitCamera failed.");
            }

            _cameraId = id;
            LoadCameraMetadata();
            ApplyDefaultRoi();
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
        var v = (long)Math.Clamp(microseconds, _expMinUs, _expMaxUs);
        if (NativeAsi.ASISetControlValue(_cameraId, ASI_CONTROL_TYPE.ASI_EXPOSURE, v, ASI_BOOL.ASI_FALSE) !=
            ASI_ERROR_CODE.ASI_SUCCESS)
            throw new InvalidOperationException("ASISetControlValue(ASI_EXPOSURE) failed.");
    }

    public double GetGain()
    {
        ThrowIfOpen();
        if (NativeAsi.ASIGetControlValue(_cameraId, ASI_CONTROL_TYPE.ASI_GAIN, out var v, out _) !=
            ASI_ERROR_CODE.ASI_SUCCESS)
            return 0;
        return v;
    }

    public void SetGain(double gain)
    {
        ThrowIfOpen();
        var v = (long)Math.Clamp((long)Math.Round(gain), _gainMin, _gainMax);
        if (NativeAsi.ASISetControlValue(_cameraId, ASI_CONTROL_TYPE.ASI_GAIN, v, ASI_BOOL.ASI_FALSE) !=
            ASI_ERROR_CODE.ASI_SUCCESS)
            throw new InvalidOperationException("ASISetControlValue(ASI_GAIN) failed.");
    }

    public void SetROI(int offsetX, int offsetY, int width, int height)
    {
        ThrowIfOpen();
        if (NativeAsi.ASISetStartPos(_cameraId, offsetX, offsetY) != ASI_ERROR_CODE.ASI_SUCCESS)
            throw new InvalidOperationException("ASISetStartPos failed.");
        if (NativeAsi.ASISetROIFormat(_cameraId, width, height, _bin, _imgType) != ASI_ERROR_CODE.ASI_SUCCESS)
            throw new InvalidOperationException("ASISetROIFormat failed.");
        if (NativeAsi.ASIGetROIFormat(_cameraId, out var w, out var h, out var bin, out var img) !=
            ASI_ERROR_CODE.ASI_SUCCESS)
            throw new InvalidOperationException("ASIGetROIFormat failed.");
        _width = w;
        _height = h;
        _bin = bin;
        _imgType = img;
    }

    public void SetBinning(int binX, int binY)
    {
        ThrowIfOpen();
        if (binX != binY)
            throw new NotSupportedException("ASI uses a single bin factor for width and height.");
        var b = Math.Clamp(binX, 1, _maxBin);
        if (NativeAsi.ASISetROIFormat(_cameraId, _width, _height, b, _imgType) != ASI_ERROR_CODE.ASI_SUCCESS)
            throw new InvalidOperationException("ASISetROIFormat (binning) failed.");
        if (NativeAsi.ASIGetROIFormat(_cameraId, out var w, out var h, out var bin, out var img) !=
            ASI_ERROR_CODE.ASI_SUCCESS)
            throw new InvalidOperationException("ASIGetROIFormat failed.");
        _width = w;
        _height = h;
        _bin = bin;
        _imgType = img;
    }

    public bool SetPixelFormat(string format)
    {
        ThrowIfOpen();
        if (!TryMapImgType(format, out var t))
            return false;
        if (NativeAsi.ASISetROIFormat(_cameraId, _width, _height, _bin, t) != ASI_ERROR_CODE.ASI_SUCCESS)
            return false;
        _imgType = t;
        return true;
    }

    public List<string> GetPixelFormatList() =>
        new() { "RAW8", "RAW16", "RGB24", "Y8" };

    public bool StartCapture(int timeoutMs = 5000)
    {
        ThrowIfOpen();
        lock (_gate)
        {
            StopCaptureInternal();
            if (NativeAsi.ASIStartVideoCapture(_cameraId) != ASI_ERROR_CODE.ASI_SUCCESS)
                return false;
            _captureRunning = true;
            _videoThread = new Thread(VideoLoop)
            {
                IsBackground = true,
                Name = "ASIVideo"
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

        var bufSize = GetBufferSizeBytes();
        var buffer = new byte[bufSize];
        while (_captureRunning)
        {
            var err = NativeAsi.ASIGetVideoData(id, buffer, bufSize, 200);
            if (err != ASI_ERROR_CODE.ASI_SUCCESS)
                continue;

            _frameSeq++;
            var fmt = _imgType switch
            {
                ASI_IMG_TYPE.ASI_IMG_RAW16 => "MONO16",
                ASI_IMG_TYPE.ASI_IMG_RGB24 => "RGB24",
                ASI_IMG_TYPE.ASI_IMG_Y8 => "MONO8",
                _ => _isColor ? "BAYERRG8" : "MONO8"
            };

            var copy = new byte[bufSize];
            Array.Copy(buffer, copy, bufSize);
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

    private int GetBufferSizeBytes() =>
        _imgType switch
        {
            ASI_IMG_TYPE.ASI_IMG_RAW16 => _width * _height * 2,
            ASI_IMG_TYPE.ASI_IMG_RGB24 => _width * _height * 3,
            _ => _width * _height
        };

    private void LoadCameraMetadata()
    {
        var info = new ASI_CAMERA_INFO();
        if (NativeAsi.ASIGetCameraProperty(ref info, IndexOfConnected(_cameraId)) != ASI_ERROR_CODE.ASI_SUCCESS)
        {
            _modelName = "ASI";
            return;
        }

        _modelName = NullTrim(info.Name);
        _sensorName = _modelName;
        _isColor = info.IsColorCam != ASI_BOOL.ASI_FALSE;
        _bayerPattern = MapBayer(info.BayerPattern);
        _pixelSizeUm = info.PixelSize;
        DiscoverBinningAndControls();
    }

    private int IndexOfConnected(int cameraId)
    {
        var n = NativeAsi.ASIGetNumOfConnectedCameras();
        for (var i = 0; i < n; i++)
        {
            var p = new ASI_CAMERA_INFO();
            if (NativeAsi.ASIGetCameraProperty(ref p, i) != ASI_ERROR_CODE.ASI_SUCCESS)
                continue;
            if (p.CameraID == cameraId)
                return i;
        }

        return 0;
    }

    private void DiscoverBinningAndControls()
    {
        if (NativeAsi.ASIGetNumOfControls(_cameraId, out var n) != ASI_ERROR_CODE.ASI_SUCCESS)
            n = 0;

        _gainMin = 0;
        _gainMax = 100;
        _expMinUs = 32;
        _expMaxUs = 2000_000;
        _maxBin = 1;

        for (var i = 0; i < n; i++)
        {
            if (NativeAsi.ASIGetControlCaps(_cameraId, i, out var caps) != ASI_ERROR_CODE.ASI_SUCCESS)
                continue;
            if (caps.ControlType == ASI_CONTROL_TYPE.ASI_GAIN)
            {
                _gainMin = caps.MinValue;
                _gainMax = caps.MaxValue;
            }
            else if (caps.ControlType == ASI_CONTROL_TYPE.ASI_EXPOSURE)
            {
                _expMinUs = caps.MinValue;
                _expMaxUs = caps.MaxValue;
            }
        }

        _maxBin = 4;
    }

    private void ApplyDefaultRoi()
    {
        var info = new ASI_CAMERA_INFO();
        if (NativeAsi.ASIGetCameraProperty(ref info, IndexOfConnected(_cameraId)) != ASI_ERROR_CODE.ASI_SUCCESS)
            throw new InvalidOperationException("ASIGetCameraProperty failed after open.");

        _bin = 1;
        _imgType = _isColor ? ASI_IMG_TYPE.ASI_IMG_RAW8 : ASI_IMG_TYPE.ASI_IMG_RAW8;
        if (NativeAsi.ASISetROIFormat(_cameraId, info.MaxWidth, info.MaxHeight, _bin, _imgType) !=
            ASI_ERROR_CODE.ASI_SUCCESS)
            throw new InvalidOperationException("ASISetROIFormat failed.");
        if (NativeAsi.ASIGetROIFormat(_cameraId, out var w, out var h, out var bin, out var img) !=
            ASI_ERROR_CODE.ASI_SUCCESS)
            throw new InvalidOperationException("ASIGetROIFormat failed.");
        _width = w;
        _height = h;
        _bin = bin;
        _imgType = img;
    }

    private void CloseCameraInternal()
    {
        StopCaptureInternal();
        if (_cameraId >= 0)
        {
            try
            {
                NativeAsi.ASICloseCamera(_cameraId);
            }
            catch
            {
                // ignore
            }

            _cameraId = -1;
        }
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
                NativeAsi.ASIStopVideoCapture(_cameraId);
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

    private static string NullTrim(string s)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;
        var i = s.IndexOf('\0');
        return i >= 0 ? s[..i] : s;
    }

    private static string MapBayer(ASI_BAYER_PATTERN p) =>
        p switch
        {
            ASI_BAYER_PATTERN.BAYER_RG => "BAYER_RG",
            ASI_BAYER_PATTERN.BAYER_BG => "BAYER_BG",
            ASI_BAYER_PATTERN.BAYER_GR => "BAYER_GR",
            ASI_BAYER_PATTERN.BAYER_GB => "BAYER_GB",
            _ => string.Empty
        };

    private static bool TryMapImgType(string format, out ASI_IMG_TYPE t)
    {
        t = ASI_IMG_TYPE.ASI_IMG_RAW8;
        if (string.IsNullOrWhiteSpace(format))
            return false;
        t = format.Trim().ToUpperInvariant() switch
        {
            "RAW8" or "BAYERRG8" => ASI_IMG_TYPE.ASI_IMG_RAW8,
            "RAW16" or "MONO16" or "BAYERRG16" => ASI_IMG_TYPE.ASI_IMG_RAW16,
            "RGB24" or "RGB8" => ASI_IMG_TYPE.ASI_IMG_RGB24,
            "Y8" or "MONO8" => ASI_IMG_TYPE.ASI_IMG_Y8,
            _ => ASI_IMG_TYPE.ASI_IMG_END
        };
        return t != ASI_IMG_TYPE.ASI_IMG_END;
    }

    private enum ASI_ERROR_CODE
    {
        ASI_SUCCESS = 0,
        ASI_ERROR_INVALID_INDEX = 1,
        ASI_ERROR_INVALID_ID = 2,
        ASI_ERROR_INVALID_CONTROL_TYPE,
        ASI_ERROR_CAMERA_CLOSED,
        ASI_ERROR_CAMERA_REMOVED,
        ASI_ERROR_INVALID_PATH,
        ASI_ERROR_INVALID_FILEFORMAT,
        ASI_ERROR_INVALID_SIZE,
        ASI_ERROR_INVALID_IMGTYPE,
        ASI_ERROR_OUTOF_BOUNDARY,
        ASI_ERROR_TIMEOUT,
        ASI_ERROR_INVALID_SEQUENCE,
        ASI_ERROR_BUFFER_TOO_SMALL,
        ASI_ERROR_VIDEO_MODE_ACTIVE,
        ASI_ERROR_EXPOSURE_IN_PROGRESS,
        ASI_ERROR_GENERAL_ERROR,
        ASI_ERROR_INVALID_MODE
    }

    private enum ASI_BOOL
    {
        ASI_FALSE = 0,
        ASI_TRUE = 1
    }

    private enum ASI_BAYER_PATTERN
    {
        BAYER_RG = 0,
        BAYER_BG,
        BAYER_GR,
        BAYER_GB
    }

    private enum ASI_IMG_TYPE
    {
        ASI_IMG_RAW8 = 0,
        ASI_IMG_RGB24,
        ASI_IMG_RAW16,
        ASI_IMG_Y8,
        ASI_IMG_END = -1
    }

    private enum ASI_CONTROL_TYPE
    {
        ASI_GAIN = 0,
        ASI_EXPOSURE,
        ASI_GAMMA,
        ASI_WB_R,
        ASI_WB_B,
        ASI_BRIGHTNESS,
        ASI_OFFSET,
        ASI_BANDWIDTHOVERLOAD,
        ASI_OVERCLOCK,
        ASI_TEMPERATURE,
        ASI_FLIP,
        ASI_AUTO_MAX_GAIN,
        ASI_AUTO_MAX_EXP,
        ASI_AUTO_TARGET_BRIGHTNESS,
        ASI_HARDWARE_BIN,
        ASI_HIGH_SPEED_MODE,
        ASI_COOLER_POWER_PERC,
        ASI_TARGET_TEMP,
        ASI_COOLER_ON_OFF,
        ASI_MONO_BIN,
        ASI_FAN_ON,
        ASI_PATTERN_ADJUST,
        ASI_ANTI_DEW_HEATER
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct ASI_CAMERA_INFO
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string Name;

        public int CameraID;
        public int MaxHeight;
        public int MaxWidth;
        public ASI_BOOL IsColorCam;
        public ASI_BAYER_PATTERN BayerPattern;

        public double PixelSize;
        public ASI_BOOL MechanicalShutter;
        public int ST4Port;
        public int ElecPerADU;
        public int BitDepth;

        public int Reserved0;
        public int Reserved1;
        public int Reserved2;
        public int Reserved3;
        public int Reserved4;
        public int Reserved5;
        public int Reserved6;
        public int Reserved7;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct ASI_CONTROL_CAPS
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string Name;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;

        public int MaxValue;
        public int MinValue;
        public int DefaultValue;
        public ASI_BOOL IsAutoSupported;
        public ASI_BOOL IsWritable;
        public ASI_CONTROL_TYPE ControlType;
        public int Unused;
    }

    private static class NativeAsi
    {
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int ASIGetNumOfConnectedCameras();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern ASI_ERROR_CODE ASIGetCameraProperty(ref ASI_CAMERA_INFO pASICameraInfo, int iCameraIndex);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern ASI_ERROR_CODE ASIOpenCamera(int iCameraID);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern ASI_ERROR_CODE ASICloseCamera(int iCameraID);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern ASI_ERROR_CODE ASIInitCamera(int iCameraID);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern ASI_ERROR_CODE ASIGetROIFormat(int iCameraID, out int piWidth, out int piHeight, out int piBin,
            out ASI_IMG_TYPE pImg_type);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern ASI_ERROR_CODE ASISetROIFormat(int iCameraID, int iWidth, int iHeight, int bin,
            ASI_IMG_TYPE Img_type);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern ASI_ERROR_CODE ASISetStartPos(int iCameraID, int startX, int startY);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern ASI_ERROR_CODE ASIGetNumOfControls(int iCameraID, out int piNumberOfControls);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern ASI_ERROR_CODE ASIGetControlCaps(int iCameraID, int iControlIndex, out ASI_CONTROL_CAPS pCaps);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern ASI_ERROR_CODE ASIGetControlValue(int iCameraID, ASI_CONTROL_TYPE ControlType, out long plValue,
            out ASI_BOOL pbAuto);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern ASI_ERROR_CODE ASISetControlValue(int iCameraID, ASI_CONTROL_TYPE ControlType, long lValue,
            ASI_BOOL bAuto);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern ASI_ERROR_CODE ASIStartVideoCapture(int iCameraID);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern ASI_ERROR_CODE ASIStopVideoCapture(int iCameraID);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern ASI_ERROR_CODE ASIGetVideoData(int iCameraID, [Out] byte[] pBuffer, int lBuffSize,
            int waitMs);

    }
}
