using System.Collections;
using System.Runtime.InteropServices;
using NINA.PL.Core;

namespace NINA.PL.Equipment.Etalon;

/// <summary>
/// ASCOM Focuser driver used as an etalon pressure tuner for solar H-alpha imaging.
/// Enumerates and connects to the same ASCOM Focuser device class but with a
/// separate ID prefix so the etalon and main focuser can be different physical devices.
/// </summary>
public sealed class AscomEtalonProvider : IEtalonProvider
{
    public const string DeviceIdPrefix = "ETALON|";

    private readonly object _sync = new();
    private dynamic? _device;
    private bool _disposed;

    public string DriverType => "ASCOM-Etalon";

    public bool IsConnected
    {
        get
        {
            lock (_sync)
            {
                if (_device is null) return false;
                try { return (bool)_device.Connected; }
                catch { return false; }
            }
        }
    }

    public int Position
    {
        get
        {
            lock (_sync)
            {
                if (_device is null) return 0;
                try { return (int)_device.Position; }
                catch { return 0; }
            }
        }
    }

    public int MaxPosition
    {
        get
        {
            lock (_sync)
            {
                if (_device is null) return 0;
                try { return (int)_device.MaxStep; }
                catch { return 0; }
            }
        }
    }

    public bool IsMoving
    {
        get
        {
            lock (_sync)
            {
                if (_device is null) return false;
                try { return (bool)_device.IsMoving; }
                catch { return false; }
            }
        }
    }

    public double Temperature
    {
        get
        {
            lock (_sync)
            {
                if (_device is null) return double.NaN;
                try { return (double)_device.Temperature; }
                catch { return double.NaN; }
            }
        }
    }

    public Task<List<EtalonDeviceInfo>> EnumerateAsync()
    {
        var list = new List<EtalonDeviceInfo>();
        try
        {
            var profileType = Type.GetTypeFromProgID("ASCOM.Utilities.Profile");
            if (profileType is null)
                return Task.FromResult(list);

            dynamic profile = Activator.CreateInstance(profileType)
                ?? throw new InvalidOperationException("Could not create ASCOM Profile.");

            object? raw = profile.RegisteredDevices("Focuser");

            if (raw is IEnumerable enumerable)
            {
                foreach (dynamic item in enumerable)
                {
                    try
                    {
                        string key = item.Key?.ToString() ?? string.Empty;
                        string val = item.Value?.ToString() ?? key;
                        if (string.IsNullOrWhiteSpace(key)) continue;
                        list.Add(new EtalonDeviceInfo
                        {
                            Id = DeviceIdPrefix + key,
                            Name = string.IsNullOrWhiteSpace(val) ? key : val,
                            DriverType = DriverType,
                            Description = key
                        });
                    }
                    catch
                    {
                        var s = item?.ToString() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(s) && s != "System.__ComObject")
                        {
                            list.Add(new EtalonDeviceInfo
                            {
                                Id = DeviceIdPrefix + s,
                                Name = s,
                                DriverType = DriverType,
                                Description = "ASCOM device"
                            });
                        }
                    }
                }
            }

            Marshal.FinalReleaseComObject(profile);
        }
        catch
        {
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
                throw new InvalidOperationException($"ASCOM Focuser ProgID not found: '{progId}'.");

            _device = Activator.CreateInstance(t)
                ?? throw new InvalidOperationException($"Failed to create ASCOM etalon: '{progId}'.");

            try { _device.Connected = true; }
            catch
            {
                try { _device.Link = true; }
                catch
                {
                    ReleaseCom(_device);
                    _device = null;
                    throw;
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        lock (_sync) DisconnectCore();
        return Task.CompletedTask;
    }

    public async Task MoveAsync(int position)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_device is null) throw new InvalidOperationException("No etalon connected.");
            _device.Move(position);
        }
        await WaitUntilStoppedAsync().ConfigureAwait(false);
    }

    public async Task MoveRelativeAsync(int offset)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_device is null) throw new InvalidOperationException("No etalon connected.");
            int target = (int)_device.Position + offset;
            _device.Move(target);
        }
        await WaitUntilStoppedAsync().ConfigureAwait(false);
    }

    public Task HaltAsync()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_device is null) throw new InvalidOperationException("No etalon connected.");
            _device.Halt();
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            DisconnectCore();
        }
        GC.SuppressFinalize(this);
    }

    private async Task WaitUntilStoppedAsync()
    {
        while (true)
        {
            bool moving;
            lock (_sync)
            {
                if (_device is null) return;
                try { moving = (bool)_device.IsMoving; }
                catch { return; }
            }
            if (!moving) return;
            await Task.Delay(50).ConfigureAwait(false);
        }
    }

    private void DisconnectCore()
    {
        if (_device is null) return;
        try { _device.Connected = false; } catch { }
        ReleaseCom(_device);
        _device = null;
    }

    private static void ReleaseCom(object? o)
    {
        if (o is null) return;
        try { if (Marshal.IsComObject(o)) Marshal.FinalReleaseComObject(o); }
        catch { }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
