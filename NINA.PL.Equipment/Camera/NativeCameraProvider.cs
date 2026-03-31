using System.Collections.Generic;
using NINA.PL.Core;

namespace NINA.PL.Equipment.Camera;

/// <summary>
/// Adapts one or more <see cref="INativeCameraBackend"/> instances to <see cref="ICameraProvider"/>.
/// </summary>
public sealed class NativeCameraProvider : ICameraProvider
{
    private readonly List<INativeCameraBackend> _backends;
    private readonly object _sync = new();

    private INativeCameraBackend? _active;
    private bool _disposed;
    private double _lastExposureUs;

    public NativeCameraProvider(IEnumerable<INativeCameraBackend> backends)
    {
        ArgumentNullException.ThrowIfNull(backends);
        _backends = backends.ToList();
    }

    public string DriverType => "Native";

    public bool IsConnected
    {
        get
        {
            lock (_sync)
                return _active is { IsConnected: true };
        }
    }

    public int SensorWidth => ActiveOrDefault(b => b.SensorWidth);

    public int SensorHeight => ActiveOrDefault(b => b.SensorHeight);

    public double PixelSizeUm => ActiveOrDefault(b => b.PixelSizeUm);

    public string ModelName => ActiveOrDefault(b => b.ModelName, string.Empty);

    public bool IsColor => ActiveOrDefault(b => b.IsColorCamera);

    public string BayerPattern => ActiveOrDefault(b => b.BayerPattern, string.Empty);

    public double ExposureMin => ActiveOrDefault(b => b.ExposureMin);

    public double ExposureMax => ActiveOrDefault(b => b.ExposureMax);

    public double GainMin => ActiveOrDefault(b => b.GainMin);

    public double GainMax => ActiveOrDefault(b => b.GainMax);

    public int MaxBinX => ActiveOrDefault(b => b.MaxBinX);

    public int MaxBinY => ActiveOrDefault(b => b.MaxBinY);

    public event EventHandler<FrameData>? FrameReceived;

    public Task<List<CameraDeviceInfo>> EnumerateAsync()
    {
        var result = new List<CameraDeviceInfo>();
        foreach (var backend in _backends)
        {
            try
            {
                backend.Initialize();
                foreach (var cam in backend.EnumerateCameras())
                {
                    var serial = cam.SerialNumber ?? string.Empty;
                    var name = string.IsNullOrEmpty(cam.ModelName) ? serial : cam.ModelName;
                    result.Add(new CameraDeviceInfo
                    {
                        Id = serial,
                        Name = name,
                        SerialNumber = serial,
                        DriverType = "Native",
                        Description = cam.VendorName ?? string.Empty
                    });
                }
            }
            catch
            {
                // Skip backends that cannot enumerate.
            }
        }

        return Task.FromResult(result);
    }

    public Task ConnectAsync(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        lock (_sync)
        {
            ThrowIfDisposed();
            DisconnectCore();

            INativeCameraBackend? chosen = null;
            foreach (var backend in _backends)
            {
                try
                {
                    backend.Initialize();
                    foreach (var cam in backend.EnumerateCameras())
                    {
                        if (string.Equals(cam.SerialNumber, deviceId, StringComparison.Ordinal))
                        {
                            chosen = backend;
                            break;
                        }
                    }
                }
                catch
                {
                    continue;
                }

                if (chosen is not null)
                    break;
            }

            if (chosen is null)
                throw new InvalidOperationException($"No native camera found with serial '{deviceId}'.");

            chosen.OpenCamera(deviceId);
            chosen.FrameArrived += OnNativeFrameArrived;
            _active = chosen;
        }

        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        lock (_sync)
        {
            DisconnectCore();
        }

        return Task.CompletedTask;
    }

    public void SetExposure(double microseconds)
    {
        _lastExposureUs = microseconds;
        WithActive(b => b.SetExposureTime(microseconds));
    }

    public void SetGain(double gain) => WithActive(b => b.SetGain(gain));

    public void SetROI(int x, int y, int width, int height) =>
        WithActive(b => b.SetROI(x, y, width, height));

    public void ResetROI()
    {
        WithActive(b => b.SetROI(0, 0, b.SensorWidth, b.SensorHeight));
    }

    public void SetBinning(int binX, int binY) => WithActive(b => b.SetBinning(binX, binY));

    public List<string> GetPixelFormats() =>
        WithActive(b => b.GetPixelFormatList(), () => new List<string>());

    public void SetPixelFormat(string format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        WithActive(b => b.SetPixelFormat(format));
    }

    public Task StartCaptureAsync()
    {
        var ok = WithActive(b => b.StartCapture(5000), () => false);
        if (!ok)
            throw new InvalidOperationException("StartCapture failed or no camera connected.");
        return Task.CompletedTask;
    }

    public Task StopCaptureAsync()
    {
        WithActive(b => b.StopCapture());
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            DisconnectCore();
            foreach (var b in _backends)
            {
                try
                {
                    b.Dispose();
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }

            _backends.Clear();
        }

        GC.SuppressFinalize(this);
    }

    private void OnNativeFrameArrived(object? sender, NativeFrameData e)
    {
        INativeCameraBackend? backend;
        lock (_sync)
            backend = _active;

        if (backend is null || !ReferenceEquals(sender, backend))
            return;

        double gain;
        try
        {
            gain = backend.GetGain();
        }
        catch
        {
            gain = 0;
        }

        var data = e.Data ?? Array.Empty<byte>();
        var frame = new FrameData
        {
            Data = data,
            Width = e.Width,
            Height = e.Height,
            PixelFormat = PixelFormatMapper.ToCore(e.PixelFormatName),
            FrameId = e.FrameId,
            Timestamp = DateTime.UtcNow,
            ExposureUs = _lastExposureUs,
            Gain = gain
        };

        FrameReceived?.Invoke(this, frame);
    }

    private void DisconnectCore()
    {
        if (_active is not null)
        {
            _active.FrameArrived -= OnNativeFrameArrived;
            try
            {
                _active.StopCapture();
            }
            catch
            {
                // ignore
            }

            try
            {
                _active.CloseCamera();
            }
            catch
            {
                // ignore
            }

            _active = null;
        }
    }

    private T ActiveOrDefault<T>(Func<INativeCameraBackend, T> selector, T whenDisconnected = default!)
    {
        lock (_sync)
        {
            if (_active is null || !_active.IsConnected)
                return whenDisconnected;
            return selector(_active);
        }
    }

    private void WithActive(Action<INativeCameraBackend> action)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_active is null || !_active.IsConnected)
                throw new InvalidOperationException("No camera connected.");
            action(_active);
        }
    }

    private T WithActive<T>(Func<INativeCameraBackend, T> func, Func<T>? whenDisconnected = null)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_active is null || !_active.IsConnected)
                return whenDisconnected is not null ? whenDisconnected() : default!;
            return func(_active);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

internal static class PixelFormatMapper
{
    public static PixelFormat ToCore(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return PixelFormat.Mono8;

        var n = name.Trim();
        // Common SDK / GenICam style names (extend as backends are added).
        return n.ToUpperInvariant() switch
        {
            "MONO8" or "MONO_8" or "GVSP_PIX_MONO8" => PixelFormat.Mono8,
            "MONO16" or "MONO_16" or "MONO16LE" or "MONO16BE" or "GVSP_PIX_MONO16" => PixelFormat.Mono16,
            "BAYERRG8" or "BAYER_RG8" or "BAYERRG" or "GVSP_PIX_BAYERRG8" => PixelFormat.BayerRG8,
            "BAYERRG16" or "BAYER_RG16" or "GVSP_PIX_BAYERRG16" => PixelFormat.BayerRG16,
            "RGB8" or "RGB8PACKED" or "RGB24" or "GVSP_PIX_RGB8" or "GVSP_PIX_RGB8_PACKED" => PixelFormat.RGB24,
            "BGR8" or "BGR8PACKED" or "BGR24" or "GVSP_PIX_BGR8" or "GVSP_PIX_BGR8_PACKED" => PixelFormat.BGR24,
            "RGB16" or "RGB48" or "RGB16PACKED" => PixelFormat.RGB48,
            "BGRA8" or "BGRA32" or "BGRA" => PixelFormat.BGRA32,
            _ => PixelFormat.Mono8
        };
    }
}
