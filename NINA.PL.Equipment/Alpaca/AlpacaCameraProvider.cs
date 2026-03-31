using System.Globalization;
using System.Text.Json;
using NINA.PL.Core;

namespace NINA.PL.Equipment.Alpaca;

/// <summary>
/// Alpaca camera implementing <see cref="ICameraProvider"/> (REST polling capture loop).
/// </summary>
public sealed class AlpacaCameraProvider : ICameraProvider
{
    public const string DeviceIdPrefix = "Alpaca|";

    private readonly object _sync = new();
    private AlpacaClient? _client;
    private bool _disposed;
    private CancellationTokenSource? _captureCts;
    private Task? _captureTask;
    private long _frameSeq;
    private double _exposureUs = 1_000_000;
    private double _gain;
    private PixelFormat _pixelFormat = PixelFormat.Mono8;
    private string _bayerPattern = string.Empty;
    private bool _isColor;
    public string DriverType => "Alpaca";

    public bool IsConnected
    {
        get
        {
            lock (_sync)
            {
                if (_client is null)
                    return false;
                try
                {
                    return _client.GetAsync<bool>("connected").GetAwaiter().GetResult() == true;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    public int SensorWidth => ReadInt32("cameraxsize");

    public int SensorHeight => ReadInt32("cameraysize");

    public double PixelSizeUm
    {
        get
        {
            try
            {
                return ReadDouble("pixelsizex");
            }
            catch
            {
                return 0;
            }
        }
    }

    public string ModelName
    {
        get
        {
            try
            {
                return ReadString("sensorname") ?? string.Empty;
            }
            catch
            {
                return string.Empty;
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
            try
            {
                return ReadDouble("exposuremin") * 1_000_000.0;
            }
            catch
            {
                return 0;
            }
        }
    }

    public double ExposureMax
    {
        get
        {
            try
            {
                return ReadDouble("exposuremax") * 1_000_000.0;
            }
            catch
            {
                return 0;
            }
        }
    }

    public double GainMin
    {
        get
        {
            try
            {
                return ReadDouble("gainmin");
            }
            catch
            {
                return 0;
            }
        }
    }

    public double GainMax
    {
        get
        {
            try
            {
                return ReadDouble("gainmax");
            }
            catch
            {
                return 0;
            }
        }
    }

    public int MaxBinX => ReadInt32("maxbinx");

    public int MaxBinY => ReadInt32("maxbiny");

    public event EventHandler<FrameData>? FrameReceived;

    public async Task<List<CameraDeviceInfo>> EnumerateAsync()
    {
        var all = await AlpacaDiscovery.DiscoverAsync().ConfigureAwait(false);
        return all
            .Where(d => string.Equals(d.DeviceType, "Camera", StringComparison.OrdinalIgnoreCase))
            .Select(d => new CameraDeviceInfo
            {
                Id = FormatDeviceId(d.ServerUrl, d.DeviceNumber, d.DeviceType),
                Name = d.Name,
                SerialNumber = d.UniqueId,
                DriverType = DriverType,
                Description = $"Alpaca {d.ServerUrl}"
            })
            .ToList();
    }

    public static string FormatDeviceId(string serverUrl, int deviceNumber, string deviceType = "Camera")
    {
        var t = deviceType.Trim();
        if (t.Equals("camera", StringComparison.OrdinalIgnoreCase))
            t = "Camera";
        return $"{DeviceIdPrefix}{t}|{serverUrl.TrimEnd('/')}|{deviceNumber.ToString(CultureInfo.InvariantCulture)}";
    }

    public static (string ServerUrl, int DeviceNumber) ParseDeviceId(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        if (!deviceId.StartsWith(DeviceIdPrefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Not an Alpaca camera device id.", nameof(deviceId));

        var rest = deviceId[DeviceIdPrefix.Length..];
        var firstSep = rest.IndexOf('|');
        if (firstSep <= 0 || firstSep >= rest.Length - 1)
            throw new ArgumentException("Invalid Alpaca camera device id.", nameof(deviceId));

        var type = rest[..firstSep];
        if (!string.Equals(type, "Camera", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Device id is not an Alpaca camera.", nameof(deviceId));

        var tail = rest[(firstSep + 1)..];
        var idx = tail.LastIndexOf('|');
        if (idx <= 0 || idx >= tail.Length - 1)
            throw new ArgumentException("Invalid Alpaca camera device id.", nameof(deviceId));

        var url = tail[..idx];
        if (!int.TryParse(tail[(idx + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var num))
            throw new ArgumentException("Invalid Alpaca camera device number.", nameof(deviceId));

        return (url, num);
    }

    public async Task ConnectAsync(string deviceId)
    {
        var (url, num) = ParseDeviceId(deviceId);
        var client = new AlpacaClient(url, "camera", num);
        try
        {
            await client.PutAsync("connected", new Dictionary<string, string> { ["Connected"] = "true" })
                .ConfigureAwait(false);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        lock (_sync)
        {
            ThrowIfDisposed();
            StopCaptureCore();
            _client?.Dispose();
            _client = client;
            UpdateSensorMapping();
            try
            {
                _gain = client.GetAsync<double>("gain").GetAwaiter().GetResult();
            }
            catch
            {
                _gain = 0;
            }
        }
    }

    public Task DisconnectAsync()
    {
        lock (_sync)
        {
            StopCaptureCore();
            TryDisconnectRemote();
            _client?.Dispose();
            _client = null;
            _isColor = false;
            _bayerPattern = string.Empty;
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
        AlpacaClient? c;
        lock (_sync)
        {
            c = _client ?? throw new InvalidOperationException("No camera connected.");
            _gain = gain;
        }

        c.PutAsync("gain", new Dictionary<string, string> { ["Gain"] = gain.ToString(CultureInfo.InvariantCulture) })
            .GetAwaiter().GetResult();
    }

    public void SetROI(int x, int y, int width, int height)
    {
        var c = ClientOrThrow();
        c.PutAsync("startx", new Dictionary<string, string> { ["StartX"] = x.ToString(CultureInfo.InvariantCulture) })
            .GetAwaiter().GetResult();
        c.PutAsync("starty", new Dictionary<string, string> { ["StartY"] = y.ToString(CultureInfo.InvariantCulture) })
            .GetAwaiter().GetResult();
        c.PutAsync("numx", new Dictionary<string, string> { ["NumX"] = width.ToString(CultureInfo.InvariantCulture) })
            .GetAwaiter().GetResult();
        c.PutAsync("numy", new Dictionary<string, string> { ["NumY"] = height.ToString(CultureInfo.InvariantCulture) })
            .GetAwaiter().GetResult();
    }

    public void ResetROI()
    {
        var c = ClientOrThrow();
        var w = c.GetAsync<int>("cameraxsize").GetAwaiter().GetResult();
        var h = c.GetAsync<int>("cameraysize").GetAwaiter().GetResult();
        SetROI(0, 0, w, h);
    }

    public void SetBinning(int binX, int binY)
    {
        var c = ClientOrThrow();
        c.PutAsync("binx", new Dictionary<string, string> { ["BinX"] = binX.ToString(CultureInfo.InvariantCulture) })
            .GetAwaiter().GetResult();
        c.PutAsync("biny", new Dictionary<string, string> { ["BinY"] = binY.ToString(CultureInfo.InvariantCulture) })
            .GetAwaiter().GetResult();
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
        AlpacaClient? cam;
        lock (_sync)
        {
            ThrowIfDisposed();
            cam = _client ?? throw new InvalidOperationException("No camera connected.");
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
                // cancelled
            }
        }

        lock (_sync)
        {
            _captureCts = new CancellationTokenSource();
            var token = _captureCts.Token;
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
                // ignore
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
            TryDisconnectRemote();
            _client?.Dispose();
            _client = null;
        }

        GC.SuppressFinalize(this);
    }

    private async Task CaptureLoopAsync(AlpacaClient client, CancellationToken ct)
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
                await client.PutAsync("startexposure", new Dictionary<string, string>
                {
                    ["Duration"] = exposureSec.ToString(CultureInfo.InvariantCulture),
                    ["Light"] = "true"
                }, ct).ConfigureAwait(false);
            }
            catch
            {
                await Task.Delay(200, ct).ConfigureAwait(false);
                continue;
            }

            while (!ct.IsCancellationRequested)
            {
                bool ready;
                try
                {
                    ready = await client.GetAsync<bool>("imageready", ct).ConfigureAwait(false) == true;
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

            int width;
            int height;
            try
            {
                width = await client.GetAsync<int>("numx", ct).ConfigureAwait(false);
                height = await client.GetAsync<int>("numy", ct).ConfigureAwait(false);
            }
            catch
            {
                continue;
            }

            byte[] data;
            PixelFormat outFmt;
            try
            {
                var json = await client.GetResponseJsonAsync("imagearray", ct).ConfigureAwait(false);
                var value = AlpacaClient.ParseValueElement(json);
                (data, outFmt) = AlpacaImageArrayParser.Flatten(value, width, height, fmt);
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

    private AlpacaClient ClientOrThrow()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            return _client ?? throw new InvalidOperationException("No camera connected.");
        }
    }

    private int ReadInt32(string property)
    {
        lock (_sync)
        {
            if (_client is null)
                return 0;
            try
            {
                return _client.GetAsync<int>(property).GetAwaiter().GetResult();
            }
            catch
            {
                return 0;
            }
        }
    }

    private double ReadDouble(string property)
    {
        lock (_sync)
        {
            if (_client is null)
                return 0;
            try
            {
                return _client.GetAsync<double>(property).GetAwaiter().GetResult();
            }
            catch
            {
                return 0;
            }
        }
    }

    private string? ReadString(string property)
    {
        lock (_sync)
        {
            if (_client is null)
                return null;
            try
            {
                return _client.GetAsync<string>(property).GetAwaiter().GetResult();
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>Alpaca / ASCOM sensortype codes (Camera).</summary>
    private void UpdateSensorMapping()
    {
        if (_client is null)
            return;

        int sensorType;
        try
        {
            sensorType = _client.GetAsync<int>("sensortype").GetAwaiter().GetResult();
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

    private void TryDisconnectRemote()
    {
        if (_client is null)
            return;
        try
        {
            _client.PutAsync("connected", new Dictionary<string, string> { ["Connected"] = "false" })
                .GetAwaiter().GetResult();
        }
        catch
        {
            // ignore
        }
    }

    private void StopCaptureCore()
    {
        _captureCts?.Cancel();
        _captureCts?.Dispose();
        _captureCts = null;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

internal static class AlpacaImageArrayParser
{
    public static (byte[] Data, PixelFormat Format) Flatten(JsonElement value, int width, int height, PixelFormat hint)
    {
        if (value.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("imagearray Value is not an array.");

        // 2D: [NumX][NumY] (ASCOM-style x-major) or [NumY][NumX] (row-major outer=y)
        var el0 = value[0];
        if (el0.ValueKind == JsonValueKind.Array)
        {
            var d0 = value.GetArrayLength();
            var d1 = el0.GetArrayLength();
            if (d0 == width && d1 == height)
                return FlattenXMajor(value, width, height, hint);
            if (d0 == height && d1 == width)
                return FlattenYMajor(value, width, height, hint);
        }

        // 1D
        var total = value.GetArrayLength();
        if (total == width * height)
            return Flatten1D(value, width, height, hint);

        throw new InvalidOperationException("imagearray dimensions do not match NumX/NumY.");
    }

    /// <summary>value[x][y] with x in [0,width), y in [0,height).</summary>
    private static (byte[] Data, PixelFormat Format) FlattenXMajor(JsonElement value, int width, int height,
        PixelFormat hint)
    {
        var max = SampleMax2D(value, width, height, (x, y) => GetDouble(value[x][y]));
        var use16 = hint is PixelFormat.Mono16 or PixelFormat.BayerRG16 or PixelFormat.RGB48 || max > 255;

        if (!use16)
        {
            var buf = new byte[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                    buf[y * width + x] = (byte)Math.Clamp((int)GetDouble(value[x][y]), 0, 255);
            }

            var fmt = hint is PixelFormat.BayerRG8 or PixelFormat.BayerRG16 ? PixelFormat.BayerRG8 : PixelFormat.Mono8;
            return (buf, fmt);
        }

        {
            var buf = new byte[width * height * 2];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var u = (ushort)Math.Clamp((int)GetDouble(value[x][y]), 0, ushort.MaxValue);
                    var i = (y * width + x) * 2;
                    buf[i] = (byte)(u & 0xFF);
                    buf[i + 1] = (byte)(u >> 8);
                }
            }

            var fmt = hint is PixelFormat.BayerRG8 or PixelFormat.BayerRG16 ? PixelFormat.BayerRG16 : PixelFormat.Mono16;
            return (buf, fmt);
        }
    }

    /// <summary>value[y][x] scanline order.</summary>
    private static (byte[] Data, PixelFormat Format) FlattenYMajor(JsonElement value, int width, int height,
        PixelFormat hint)
    {
        var max = SampleMax2D(value, height, width, (y, x) => GetDouble(value[y][x]));
        var use16 = hint is PixelFormat.Mono16 or PixelFormat.BayerRG16 or PixelFormat.RGB48 || max > 255;

        if (!use16)
        {
            var buf = new byte[width * height];
            for (var y = 0; y < height; y++)
            {
                var row = value[y];
                for (var x = 0; x < width; x++)
                    buf[y * width + x] = (byte)Math.Clamp((int)GetDouble(row[x]), 0, 255);
            }

            var fmt = hint is PixelFormat.BayerRG8 or PixelFormat.BayerRG16 ? PixelFormat.BayerRG8 : PixelFormat.Mono8;
            return (buf, fmt);
        }

        {
            var buf = new byte[width * height * 2];
            for (var y = 0; y < height; y++)
            {
                var row = value[y];
                for (var x = 0; x < width; x++)
                {
                    var u = (ushort)Math.Clamp((int)GetDouble(row[x]), 0, ushort.MaxValue);
                    var i = (y * width + x) * 2;
                    buf[i] = (byte)(u & 0xFF);
                    buf[i + 1] = (byte)(u >> 8);
                }
            }

            var fmt = hint is PixelFormat.BayerRG8 or PixelFormat.BayerRG16 ? PixelFormat.BayerRG16 : PixelFormat.Mono16;
            return (buf, fmt);
        }
    }

    private static double SampleMax2D(JsonElement value, int outer, int inner, Func<int, int, double> read)
    {
        var max = 0.0;
        for (var i = 0; i < Math.Min(outer, 32); i++)
        {
            for (var j = 0; j < Math.Min(inner, 32); j++)
                max = Math.Max(max, Math.Abs(read(i, j)));
        }

        return max;
    }

    private static (byte[] Data, PixelFormat Format) Flatten1D(JsonElement value, int width, int height, PixelFormat hint)
    {
        var max = 0.0;
        var n = Math.Min(width * height, 1024);
        for (var i = 0; i < n; i++)
            max = Math.Max(max, Math.Abs(GetDouble(value[i])));

        var use16 = hint is PixelFormat.Mono16 or PixelFormat.BayerRG16 or PixelFormat.RGB48 || max > 255;
        if (!use16)
        {
            var buf = new byte[width * height];
            for (var i = 0; i < buf.Length; i++)
                buf[i] = (byte)Math.Clamp((int)GetDouble(value[i]), 0, 255);
            var fmt = hint is PixelFormat.BayerRG8 or PixelFormat.BayerRG16 ? PixelFormat.BayerRG8 : PixelFormat.Mono8;
            return (buf, fmt);
        }

        {
            var buf = new byte[width * height * 2];
            var idx = 0;
            for (var i = 0; i < width * height; i++)
            {
                var u = (ushort)Math.Clamp((int)GetDouble(value[i]), 0, ushort.MaxValue);
                buf[idx++] = (byte)(u & 0xFF);
                buf[idx++] = (byte)(u >> 8);
            }

            var fmt = hint is PixelFormat.BayerRG8 or PixelFormat.BayerRG16 ? PixelFormat.BayerRG16 : PixelFormat.Mono16;
            return (buf, fmt);
        }
    }

    private static double GetDouble(JsonElement e) =>
        e.ValueKind switch
        {
            JsonValueKind.Number => e.GetDouble(),
            JsonValueKind.String => double.TryParse(e.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                ? d
                : 0,
            _ => 0
        };
}
