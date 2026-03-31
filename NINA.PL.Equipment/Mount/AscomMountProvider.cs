using System.Collections;
using System.Runtime.InteropServices;
using NINA.PL.Core;

namespace NINA.PL.Equipment.Mount;

/// <summary>
/// ASCOM Telescope driver via COM late-binding.
/// </summary>
public sealed class AscomMountProvider : IMountProvider
{
    public const string DeviceIdPrefix = "ASCOM|";

    private readonly object _sync = new();
    private dynamic? _telescope;
    private bool _disposed;

    public string DriverType => "ASCOM";

    public bool IsConnected
    {
        get
        {
            lock (_sync)
            {
                if (_telescope is null)
                    return false;
                try
                {
                    return (bool)_telescope.Connected;
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
            lock (_sync)
            {
                if (_telescope is null)
                    return false;
                try
                {
                    return (bool)_telescope.Tracking;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    public double RightAscension
    {
        get
        {
            lock (_sync)
            {
                if (_telescope is null)
                    return 0;
                try
                {
                    return (double)_telescope.RightAscension;
                }
                catch
                {
                    return 0;
                }
            }
        }
    }

    public double Declination
    {
        get
        {
            lock (_sync)
            {
                if (_telescope is null)
                    return 0;
                try
                {
                    return (double)_telescope.Declination;
                }
                catch
                {
                    return 0;
                }
            }
        }
    }

    public double Altitude
    {
        get
        {
            lock (_sync)
            {
                if (_telescope is null)
                    return 0;
                try
                {
                    return (double)_telescope.Altitude;
                }
                catch
                {
                    return 0;
                }
            }
        }
    }

    public double Azimuth
    {
        get
        {
            lock (_sync)
            {
                if (_telescope is null)
                    return 0;
                try
                {
                    return (double)_telescope.Azimuth;
                }
                catch
                {
                    return 0;
                }
            }
        }
    }

    public bool CanPulseGuide
    {
        get
        {
            lock (_sync)
            {
                if (_telescope is null)
                    return false;
                try
                {
                    return (bool)_telescope.CanPulseGuide;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    public Task<List<MountDeviceInfo>> EnumerateAsync()
    {
        var list = new List<MountDeviceInfo>();
        try
        {
            var profileType = Type.GetTypeFromProgID("ASCOM.Utilities.Profile");
            if (profileType is null)
                return Task.FromResult(list);

            dynamic profile = Activator.CreateInstance(profileType)
                ?? throw new InvalidOperationException("Could not create ASCOM Profile.");

            object? raw = profile.RegisteredDevices("Telescope");
            void AddId(string? s)
            {
                if (string.IsNullOrWhiteSpace(s))
                    return;
                list.Add(new MountDeviceInfo
                {
                    Id = DeviceIdPrefix + s,
                    Name = s,
                    DriverType = DriverType,
                    Description = "ASCOM Telescope"
                });
            }

            if (raw is string[] ids)
            {
                foreach (var id in ids)
                    AddId(id);
            }
            else if (raw is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                    AddId(item?.ToString());
            }

            Marshal.FinalReleaseComObject(profile);
        }
        catch
        {
            // ASCOM not available
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
            DisconnectCore();

            var t = Type.GetTypeFromProgID(progId.Trim());
            if (t is null)
                throw new InvalidOperationException($"ASCOM Telescope ProgID not found: '{progId}'.");

            _telescope = Activator.CreateInstance(t)
                ?? throw new InvalidOperationException($"Failed to create ASCOM telescope: '{progId}'.");

            try
            {
                _telescope.Connected = true;
            }
            catch
            {
                ReleaseCom(_telescope);
                _telescope = null;
                throw;
            }
        }

        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        lock (_sync)
            DisconnectCore();
        return Task.CompletedTask;
    }

    public Task SlewToCoordinatesAsync(double ra, double dec)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_telescope is null)
                throw new InvalidOperationException("No mount connected.");
            _telescope.SlewToCoordinates(ra, dec);
        }

        return Task.CompletedTask;
    }

    public Task SlewToAltAzAsync(double altitudeDegrees, double azimuthDegrees)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_telescope is null)
                throw new InvalidOperationException("No mount connected.");
            // ASCOM ITelescopeV3: SlewToAltAz(Azimuth, Altitude)
            _telescope.SlewToAltAz(azimuthDegrees, altitudeDegrees);
        }

        return Task.CompletedTask;
    }

    public Task PulseGuideAsync(GuideDirection direction, int durationMs)
    {
        var guideDir = ToAscomGuideDirection(direction);
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_telescope is null)
                throw new InvalidOperationException("No mount connected.");
            _telescope.PulseGuide(guideDir, durationMs);
        }

        return Task.CompletedTask;
    }

    public Task SetTrackingAsync(bool enabled)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_telescope is null)
                throw new InvalidOperationException("No mount connected.");
            _telescope.Tracking = enabled;
        }

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
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>ASCOM GuideDirections: guideNorth=0, guideSouth=1, guideEast=2, guideWest=3.</summary>
    private static int ToAscomGuideDirection(GuideDirection d) =>
        d switch
        {
            GuideDirection.North => 0,
            GuideDirection.South => 1,
            GuideDirection.East => 2,
            GuideDirection.West => 3,
            _ => 0
        };

    private void DisconnectCore()
    {
        if (_telescope is null)
            return;
        try
        {
            _telescope.Connected = false;
        }
        catch
        {
            // ignore
        }

        ReleaseCom(_telescope);
        _telescope = null;
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
