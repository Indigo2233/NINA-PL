using System.Collections;
using System.Runtime.InteropServices;
using NINA.PL.Core;

namespace NINA.PL.Equipment.Focuser;

/// <summary>
/// ASCOM Focuser driver via COM late-binding.
/// </summary>
public sealed class AscomFocuserProvider : IFocuserProvider
{
    public const string DeviceIdPrefix = "ASCOM|";

    private readonly object _sync = new();
    private dynamic? _focuser;
    private bool _disposed;

    public string DriverType => "ASCOM";

    public bool IsConnected
    {
        get
        {
            lock (_sync)
            {
                if (_focuser is null)
                    return false;
                try
                {
                    return (bool)_focuser.Connected;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    public int Position
    {
        get
        {
            lock (_sync)
            {
                if (_focuser is null)
                    return 0;
                try
                {
                    return (int)_focuser.Position;
                }
                catch
                {
                    return 0;
                }
            }
        }
    }

    public int MaxPosition
    {
        get
        {
            lock (_sync)
            {
                if (_focuser is null)
                    return 0;
                try
                {
                    return (int)_focuser.MaxStep;
                }
                catch
                {
                    return 0;
                }
            }
        }
    }

    public bool IsMoving
    {
        get
        {
            lock (_sync)
            {
                if (_focuser is null)
                    return false;
                try
                {
                    return (bool)_focuser.IsMoving;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    public double Temperature
    {
        get
        {
            lock (_sync)
            {
                if (_focuser is null)
                    return double.NaN;
                try
                {
                    return (double)_focuser.Temperature;
                }
                catch
                {
                    return double.NaN;
                }
            }
        }
    }

    public Task<List<FocuserDeviceInfo>> EnumerateAsync()
    {
        var list = new List<FocuserDeviceInfo>();
        try
        {
            var profileType = Type.GetTypeFromProgID("ASCOM.Utilities.Profile");
            if (profileType is null)
                return Task.FromResult(list);

            dynamic profile = Activator.CreateInstance(profileType)
                ?? throw new InvalidOperationException("Could not create ASCOM Profile.");

            object? raw = profile.RegisteredDevices("Focuser");
            void AddId(string? s)
            {
                if (string.IsNullOrWhiteSpace(s))
                    return;
                list.Add(new FocuserDeviceInfo
                {
                    Id = DeviceIdPrefix + s,
                    Name = s,
                    DriverType = DriverType
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
                throw new InvalidOperationException($"ASCOM Focuser ProgID not found: '{progId}'.");

            _focuser = Activator.CreateInstance(t)
                ?? throw new InvalidOperationException($"Failed to create ASCOM focuser: '{progId}'.");

            try
            {
                _focuser.Connected = true;
            }
            catch
            {
                ReleaseCom(_focuser);
                _focuser = null;
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

    public async Task MoveAsync(int position)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_focuser is null)
                throw new InvalidOperationException("No focuser connected.");
            _focuser.Move(position);
        }

        await WaitUntilStoppedAsync().ConfigureAwait(false);
    }

    public async Task MoveRelativeAsync(int offset)
    {
        int target;
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_focuser is null)
                throw new InvalidOperationException("No focuser connected.");
            target = (int)_focuser.Position + offset;
            _focuser.Move(target);
        }

        await WaitUntilStoppedAsync().ConfigureAwait(false);
    }

    public Task HaltAsync()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_focuser is null)
                throw new InvalidOperationException("No focuser connected.");
            _focuser.Halt();
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

    private async Task WaitUntilStoppedAsync()
    {
        while (true)
        {
            bool moving;
            lock (_sync)
            {
                if (_focuser is null)
                    return;
                try
                {
                    moving = (bool)_focuser.IsMoving;
                }
                catch
                {
                    return;
                }
            }

            if (!moving)
                return;
            await Task.Delay(50).ConfigureAwait(false);
        }
    }

    private void DisconnectCore()
    {
        if (_focuser is null)
            return;
        try
        {
            _focuser.Connected = false;
        }
        catch
        {
            // ignore
        }

        ReleaseCom(_focuser);
        _focuser = null;
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
