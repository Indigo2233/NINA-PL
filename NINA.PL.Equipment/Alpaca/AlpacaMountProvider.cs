using System.Globalization;
using System.Linq;
using NINA.PL.Core;

namespace NINA.PL.Equipment.Alpaca;

/// <summary>
/// Alpaca telescope implementing <see cref="IMountProvider"/>.
/// </summary>
public sealed class AlpacaMountProvider : IMountProvider
{
    public const string DeviceIdPrefix = "Alpaca|";

    private readonly object _sync = new();
    private AlpacaClient? _client;
    private bool _disposed;

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

    public bool IsTracking
    {
        get
        {
            try
            {
                return ReadBool("tracking");
            }
            catch
            {
                return false;
            }
        }
    }

    public double RightAscension => ReadDouble("rightascension");

    public double Declination => ReadDouble("declination");

    public double Altitude => ReadDouble("altitude");

    public double Azimuth => ReadDouble("azimuth");

    public bool CanPulseGuide
    {
        get
        {
            try
            {
                return ReadBool("canpulseguide");
            }
            catch
            {
                return false;
            }
        }
    }

    public async Task<List<MountDeviceInfo>> EnumerateAsync()
    {
        var all = await AlpacaDiscovery.DiscoverAsync().ConfigureAwait(false);
        return all
            .Where(d => string.Equals(d.DeviceType, "Telescope", StringComparison.OrdinalIgnoreCase))
            .Select(d => new MountDeviceInfo
            {
                Id = FormatDeviceId(d.ServerUrl, d.DeviceNumber, d.DeviceType),
                Name = d.Name,
                DriverType = DriverType,
                Description = $"Alpaca {d.ServerUrl}"
            })
            .ToList();
    }

    public static string FormatDeviceId(string serverUrl, int deviceNumber, string deviceType = "Telescope")
    {
        var t = deviceType.Trim();
        if (t.Equals("telescope", StringComparison.OrdinalIgnoreCase))
            t = "Telescope";
        return $"{DeviceIdPrefix}{t}|{serverUrl.TrimEnd('/')}|{deviceNumber.ToString(CultureInfo.InvariantCulture)}";
    }

    public static (string ServerUrl, int DeviceNumber) ParseDeviceId(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        if (!deviceId.StartsWith(DeviceIdPrefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Not an Alpaca mount device id.", nameof(deviceId));

        var rest = deviceId[DeviceIdPrefix.Length..];
        var firstSep = rest.IndexOf('|');
        if (firstSep <= 0 || firstSep >= rest.Length - 1)
            throw new ArgumentException("Invalid Alpaca mount device id.", nameof(deviceId));

        var type = rest[..firstSep];
        if (!string.Equals(type, "Telescope", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Device id is not an Alpaca telescope.", nameof(deviceId));

        var tail = rest[(firstSep + 1)..];
        var idx = tail.LastIndexOf('|');
        if (idx <= 0 || idx >= tail.Length - 1)
            throw new ArgumentException("Invalid Alpaca mount device id.", nameof(deviceId));

        var url = tail[..idx];
        if (!int.TryParse(tail[(idx + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var num))
            throw new ArgumentException("Invalid Alpaca mount device number.", nameof(deviceId));

        return (url, num);
    }

    public async Task ConnectAsync(string deviceId)
    {
        var (url, num) = ParseDeviceId(deviceId);
        var client = new AlpacaClient(url, "telescope", num);
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
            _client?.Dispose();
            _client = client;
        }
    }

    public Task DisconnectAsync()
    {
        lock (_sync)
        {
            TryDisconnectRemote();
            _client?.Dispose();
            _client = null;
        }

        return Task.CompletedTask;
    }

    public Task SlewToCoordinatesAsync(double ra, double dec)
    {
        var c = ClientOrThrow();
        return c.PutAsync("slewtocoordinates", new Dictionary<string, string>
        {
            ["RightAscension"] = ra.ToString(CultureInfo.InvariantCulture),
            ["Declination"] = dec.ToString(CultureInfo.InvariantCulture)
        });
    }

    public Task SlewToAltAzAsync(double altitudeDegrees, double azimuthDegrees)
    {
        var c = ClientOrThrow();
        return c.PutAsync("slewtoaltaz", new Dictionary<string, string>
        {
            ["Azimuth"] = azimuthDegrees.ToString(CultureInfo.InvariantCulture),
            ["Altitude"] = altitudeDegrees.ToString(CultureInfo.InvariantCulture),
        });
    }

    public Task PulseGuideAsync(GuideDirection direction, int durationMs)
    {
        var c = ClientOrThrow();
        return c.PutAsync("pulseguide", new Dictionary<string, string>
        {
            ["Direction"] = ((int)direction).ToString(CultureInfo.InvariantCulture),
            ["Duration"] = durationMs.ToString(CultureInfo.InvariantCulture)
        });
    }

    public Task SetTrackingAsync(bool enabled)
    {
        var c = ClientOrThrow();
        return c.PutAsync("tracking", new Dictionary<string, string>
        {
            ["Tracking"] = enabled ? "true" : "false"
        });
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            TryDisconnectRemote();
            _client?.Dispose();
            _client = null;
        }

        GC.SuppressFinalize(this);
    }

    private AlpacaClient ClientOrThrow()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            return _client ?? throw new InvalidOperationException("No mount connected.");
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

    private bool ReadBool(string property)
    {
        lock (_sync)
        {
            if (_client is null)
                return false;
            try
            {
                return _client.GetAsync<bool>(property).GetAwaiter().GetResult();
            }
            catch
            {
                return false;
            }
        }
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

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
