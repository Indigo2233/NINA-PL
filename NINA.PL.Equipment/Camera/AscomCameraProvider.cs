using System.Collections;
using System.Runtime.InteropServices;
using NINA.PL.Core;

namespace NINA.PL.Equipment.Camera;

/// <summary>
/// ASCOM Camera driver via COM late-binding (no reference to ASCOM Platform assemblies).
/// </summary>
public sealed class AscomCameraProvider : ICameraProvider
{
    public const string DeviceIdPrefix = "ASCOM|";

    private readonly object _sync = new();
    private dynamic? _camera;
    private bool _disposed;
    private CancellationTokenSource? _captureCts;
    private Task? _captureTask;
    private long _frameSeq;
    private double _exposureUs = 1_000_000;
    private double _gain;
    private PixelFormat _pixelFormat = PixelFormat.Mono8;
    private string _bayerPattern = string.Empty;
    private bool _isColor;

    public string DriverType => "ASCOM";

    public bool IsConnected
    {
        get
        {
            lock (_sync)
            {
                if (_camera is null)
                    return false;
                try
                {
                    return (bool)_camera.Connected;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    public int SensorWidth => ReadCameraInt(c => c.CameraXSize, 0);

    public int SensorHeight => ReadCameraInt(c => c.CameraYSize, 0);

    public double PixelSizeUm
    {
        get
        {
            lock (_sync)
            {
                if (_camera is null)
                    return 0;
                try
                {
                    return (double)_camera.PixelSizeX;
                }
                catch
                {
                    return 0;
                }
            }
        }
    }

    public string ModelName
    {
        get
        {
            lock (_sync)
            {
                if (_camera is null)
                    return string.Empty;
                try
                {
                    return (string)_camera.Description ?? string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }
        }
    }

    public bool IsColor
    {
        get
        {
            lock (_sync)
                return _isColor;
        }
    }

    public string BayerPattern
    {
        get
        {
            lock (_sync)
                return _bayerPattern;
        }
    }

    public double ExposureMin
    {
        get
        {
            lock (_sync)
            {
                if (_camera is null)
                    return 0;
                try
                {
                    return (double)_camera.ExposureMin * 1_000_000.0;
                }
                catch
                {
                    return 0;
                }
            }
        }
    }

    public double ExposureMax
    {
        get
        {
            lock (_sync)
            {
                if (_camera is null)
                    return 0;
                try
                {
                    return (double)_camera.ExposureMax * 1_000_000.0;
                }
                catch
                {
                    return 0;
                }
            }
        }
    }

    public double GainMin
    {
        get
        {
            lock (_sync)
            {
                if (_camera is null)
                    return 0;
                try
                {
                    return (double)_camera.GainMin;
                }
                catch
                {
                    return 0;
                }
            }
        }
    }

    public double GainMax
    {
        get
        {
            lock (_sync)
            {
                if (_camera is null)
                    return 0;
                try
                {
                    return (double)_camera.GainMax;
                }
                catch
                {
                    return 0;
                }
            }
        }
    }

    public int MaxBinX => ReadCameraInt(c => c.MaxBinX, 1);

    public int MaxBinY => ReadCameraInt(c => c.MaxBinY, 1);

    public event EventHandler<FrameData>? FrameReceived;

    public Task<List<CameraDeviceInfo>> EnumerateAsync()
    {
        var list = new List<CameraDeviceInfo>();
        try
        {
            var profileType = Type.GetTypeFromProgID("ASCOM.Utilities.Profile");
            if (profileType is null)
                return Task.FromResult(list);

            dynamic profile = Activator.CreateInstance(profileType)
                ?? throw new InvalidOperationException("Could not create ASCOM Profile.");

            object? raw = profile.RegisteredDevices("Camera");
            if (raw is string[] ids)
            {
                foreach (var id in ids)
                {
                    if (string.IsNullOrWhiteSpace(id))
                        continue;
                    list.Add(new CameraDeviceInfo
                    {
                        Id = DeviceIdPrefix + id,
                        Name = id,
                        SerialNumber = string.Empty,
                        DriverType = DriverType,
                        Description = "ASCOM Camera"
                    });
                }
            }
            else if (raw is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    var s = item?.ToString();
                    if (string.IsNullOrWhiteSpace(s))
                        continue;
                    list.Add(new CameraDeviceInfo
                    {
                        Id = DeviceIdPrefix + s,
                        Name = s,
                        SerialNumber = string.Empty,
                        DriverType = DriverType,
                        Description = "ASCOM Camera"
                    });
                }
            }

            Marshal.FinalReleaseComObject(profile);
        }
        catch
        {
            // ASCOM not installed or Profile unavailable.
        }

        return Task.FromResult(list);
    }

    public Task ConnectAsync(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        var progId = deviceId.StartsWith(DeviceIdPrefix, StringComparison.OrdinalIgnoreCase)
            ? deviceId[DeviceIdPrefix.Length..]
            : deviceId;

        lock (_sync)
        {
            ThrowIfDisposed();
            StopCaptureCore();
            DisconnectCore();

            var t = Type.GetTypeFromProgID(progId.Trim());
            if (t is null)
                throw new InvalidOperationException($"ASCOM Camera ProgID not found: '{progId}'.");

            _camera = Activator.CreateInstance(t)
                ?? throw new InvalidOperationException($"Failed to create ASCOM camera: '{progId}'.");

            try
            {
                _camera.Connected = true;
                UpdateSensorMapping();
                try
                {
                    _gain = (double)_camera.Gain;
                }
                catch
                {
                    _gain = 0;
                }
            }
            catch
            {
                try
                {
                    _camera.Connected = false;
                }
                catch
                {
                    // ignore
                }

                ReleaseCom(_camera);
                _camera = null;
                throw;
            }
        }

        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        lock (_sync)
        {
            StopCaptureCore();
            DisconnectCore();
        }

        return Task.CompletedTask;
    }

    public void SetExposure(double microseconds)
    {
        var min = ExposureMin;
        var max = ExposureMax;
        if (max > 0 && min <= max)
            _exposureUs = Math.Clamp(microseconds, min, max);
        else
            _exposureUs = microseconds;
    }

    public void SetGain(double gain)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_camera is null)
                throw new InvalidOperationException("No camera connected.");
            _camera.Gain = (int)Math.Round(gain);
            _gain = gain;
        }
    }

    public void SetROI(int x, int y, int width, int height)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_camera is null)
                throw new InvalidOperationException("No camera connected.");
            _camera.StartX = x;
            _camera.StartY = y;
            _camera.NumX = width;
            _camera.NumY = height;
        }
    }

    public void ResetROI()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_camera is null)
                throw new InvalidOperationException("No camera connected.");
            _camera.StartX = 0;
            _camera.StartY = 0;
            _camera.NumX = Convert.ToInt32(_camera.CameraXSize);
            _camera.NumY = Convert.ToInt32(_camera.CameraYSize);
        }
    }

    public void SetBinning(int binX, int binY)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_camera is null)
                throw new InvalidOperationException("No camera connected.");
            _camera.BinX = binX;
            _camera.BinY = binY;
        }
    }

    public List<string> GetPixelFormats()
    {
        lock (_sync)
        {
            var formats = new List<string> { "Mono8", "Mono16", "BayerRG8", "BayerRG16" };
            if (!_isColor)
                formats.RemoveAll(f => f.StartsWith("Bayer", StringComparison.OrdinalIgnoreCase));
            return formats;
        }
    }

    public void SetPixelFormat(string format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        lock (_sync)
        {
            var f = format.Trim();
            _pixelFormat = f.ToUpperInvariant() switch
            {
                "MONO8" => PixelFormat.Mono8,
                "MONO16" => PixelFormat.Mono16,
                "BAYERRG8" or "BAYER_RG8" => PixelFormat.BayerRG8,
                "BAYERRG16" or "BAYER_RG16" => PixelFormat.BayerRG16,
                "RGB24" => PixelFormat.RGB24,
                "BGR24" => PixelFormat.BGR24,
                "RGB48" => PixelFormat.RGB48,
                "BGRA32" => PixelFormat.BGRA32,
                _ => _pixelFormat
            };
        }
    }

    public async Task StartCaptureAsync()
    {
        Task? previous;
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_camera is null || !(bool)_camera.Connected)
                throw new InvalidOperationException("No camera connected.");

            previous = _captureTask;
            StopCaptureCore();
            _captureTask = null;
        }

        if (previous is not null)
        {
            try
            {
                await previous.ConfigureAwait(false);
            }
            catch
            {
                // previous loop ended with cancel/error
            }
        }

        lock (_sync)
        {
            ThrowIfDisposed();
            if (_camera is null || !(bool)_camera.Connected)
                throw new InvalidOperationException("No camera connected.");

            _captureCts = new CancellationTokenSource();
            var token = _captureCts.Token;
            var cam = _camera;
            _captureTask = Task.Run(() => CaptureLoopAsync(cam, token), token);
        }
    }

    public async Task StopCaptureAsync()
    {
        Task? t;
        lock (_sync)
        {
            StopCaptureCore();
            t = _captureTask;
            _captureTask = null;
        }

        if (t is not null)
        {
            try
            {
                await t.ConfigureAwait(false);
            }
            catch
            {
                // loop cancelled
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            StopCaptureCore();
            DisconnectCore();
        }

        GC.SuppressFinalize(this);
    }

    private async Task CaptureLoopAsync(dynamic camera, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            double exposureSec;
            double exposureUsSnapshot;
            double gainSnapshot;
            PixelFormat fmt;
            lock (_sync)
            {
                exposureUsSnapshot = _exposureUs;
                exposureSec = exposureUsSnapshot / 1_000_000.0;
                if (exposureSec <= 0)
                    exposureSec = 0.001;
                gainSnapshot = _gain;
                fmt = _pixelFormat;
            }

            try
            {
                camera.StartExposure(exposureSec, true);
            }
            catch
            {
                await Task.Delay(200, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                    break;
                continue;
            }

            while (!ct.IsCancellationRequested)
            {
                bool ready = false;
                try
                {
                    ready = (bool)camera.ImageReady;
                }
                catch
                {
                    break;
                }

                if (ready)
                    break;
                await Task.Delay(10, ct).ConfigureAwait(false);
            }

            if (ct.IsCancellationRequested)
                break;

            object? arrObj;
            try
            {
                arrObj = camera.ImageArray;
            }
            catch
            {
                continue;
            }

            if (arrObj is not Array arr || arr.Rank != 2)
                continue;

            int width;
            int height;
            try
            {
                width = (int)camera.NumX;
                height = (int)camera.NumY;
            }
            catch
            {
                width = arr.GetLength(0);
                height = arr.GetLength(1);
            }

            byte[] data;
            PixelFormat outFmt;
            try
            {
                (data, outFmt) = FlattenAscomImageArray(arr, width, height, fmt);
            }
            catch
            {
                continue;
            }

            var id = (ulong)Interlocked.Increment(ref _frameSeq);
            var frame = new FrameData
            {
                Data = data,
                Width = width,
                Height = height,
                PixelFormat = outFmt,
                FrameId = id,
                Timestamp = DateTime.UtcNow,
                ExposureUs = exposureUsSnapshot,
                Gain = gainSnapshot
            };

            FrameReceived?.Invoke(this, frame);
        }
    }

    private static (byte[] Data, PixelFormat Format) FlattenAscomImageArray(Array arr, int width, int height, PixelFormat requested)
    {
        var et = arr.GetType().GetElementType();
        var use16 = requested is PixelFormat.Mono16 or PixelFormat.BayerRG16 or PixelFormat.RGB48;
        if (et == typeof(byte) || et == typeof(sbyte))
        {
            var buf = new byte[width * height];
            int i = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var v = arr.GetValue(x, y);
                    buf[i++] = v is byte b ? b : Convert.ToByte(v);
                }
            }

            var outFmt = requested is PixelFormat.BayerRG8 or PixelFormat.BayerRG16
                ? PixelFormat.BayerRG8
                : PixelFormat.Mono8;
            return (buf, outFmt);
        }

        if (use16 || et == typeof(short) || et == typeof(ushort) || et == typeof(int) || et == typeof(uint) ||
            et == typeof(long) || et == typeof(ulong) || et == typeof(double) || et == typeof(float))
        {
            var buf = new byte[width * height * 2];
            int i = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var v = arr.GetValue(x, y);
                    ushort u = v switch
                    {
                        ushort us => us,
                        short s => (ushort)s,
                        int n => (ushort)Math.Clamp(n, 0, ushort.MaxValue),
                        uint ui => (ushort)Math.Min(ui, ushort.MaxValue),
                        double d => (ushort)Math.Clamp((int)d, 0, ushort.MaxValue),
                        float f => (ushort)Math.Clamp((int)f, 0, ushort.MaxValue),
                        _ => Convert.ToUInt16(v)
                    };
                    buf[i++] = (byte)(u & 0xFF);
                    buf[i++] = (byte)(u >> 8);
                }
            }

            var fmt = requested is PixelFormat.BayerRG8 ? PixelFormat.BayerRG16 : PixelFormat.Mono16;
            if (requested is PixelFormat.BayerRG8 or PixelFormat.BayerRG16)
                fmt = PixelFormat.BayerRG16;
            return (buf, fmt);
        }

        throw new NotSupportedException($"ASCOM ImageArray element type '{et?.Name}' is not supported.");
    }

    /// <summary>ASCOM SensorType: 0=Monochrome, 1=Color, 2=RGGB, 3=CMYG, 4=CMYG2, 5=LRGB.</summary>
    private void UpdateSensorMapping()
    {
        if (_camera is null)
            return;

        int sensorType;
        try
        {
            sensorType = (int)_camera.SensorType;
        }
        catch
        {
            sensorType = 0;
        }

        _isColor = sensorType != 0;
        _bayerPattern = sensorType switch
        {
            1 => "RGB",
            2 => "RGGB",
            3 => "CMYG",
            4 => "CMYG2",
            5 => "LRGB",
            _ => string.Empty
        };

        _pixelFormat = sensorType switch
        {
            0 => PixelFormat.Mono16,
            2 => PixelFormat.BayerRG16,
            _ => PixelFormat.Mono16
        };
    }

    private int ReadCameraInt(Func<dynamic, object> read, int fallback)
    {
        lock (_sync)
        {
            if (_camera is null)
                return fallback;
            try
            {
                return Convert.ToInt32(read(_camera));
            }
            catch
            {
                return fallback;
            }
        }
    }

    private void StopCaptureCore()
    {
        _captureCts?.Cancel();
        _captureCts?.Dispose();
        _captureCts = null;
    }

    private void DisconnectCore()
    {
        if (_camera is null)
            return;
        try
        {
            _camera.Connected = false;
        }
        catch
        {
            // ignore
        }

        ReleaseCom(_camera);
        _camera = null;
        _isColor = false;
        _bayerPattern = string.Empty;
    }

    private static void ReleaseCom(object? o)
    {
        if (o is null)
            return;
        try
        {
            if (Marshal.IsComObject(o))
                Marshal.FinalReleaseComObject(o);
        }
        catch
        {
            // ignore
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
