using System.Collections;
using System.Runtime.InteropServices;
using NINA.PL.Core;

namespace NINA.PL.Equipment.FilterWheel;

/// <summary>
/// ASCOM FilterWheel driver via COM late-binding.
/// </summary>
public sealed class AscomFilterWheelProvider : IFilterWheelProvider
{
    public const string DeviceIdPrefix = "ASCOM|";

    private readonly object _sync = new();
    private dynamic? _wheel;
    private bool _disposed;
    private List<string> _filterNames = [];

    public string DriverType => "ASCOM";

    public bool IsConnected
    {
        get
        {
            lock (_sync)
            {
                if (_wheel is null)
                    return false;
                try
                {
                    return (bool)_wheel.Connected;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    public int CurrentPosition
    {
        get
        {
            lock (_sync)
            {
                if (_wheel is null)
                    return 0;
                try
                {
                    return (int)_wheel.Position;
                }
                catch
                {
                    return 0;
                }
            }
        }
    }

    public List<string> FilterNames
    {
        get
        {
            lock (_sync)
                return new List<string>(_filterNames);
        }
    }

    public Task<List<FilterWheelDeviceInfo>> EnumerateAsync()
    {
        var list = new List<FilterWheelDeviceInfo>();
        try
        {
            var profileType = Type.GetTypeFromProgID("ASCOM.Utilities.Profile");
            if (profileType is null)
                return Task.FromResult(list);

            dynamic profile = Activator.CreateInstance(profileType)
                ?? throw new InvalidOperationException("Could not create ASCOM Profile.");

            object? raw = profile.RegisteredDevices("FilterWheel");
            void AddId(string? s)
            {
                if (string.IsNullOrWhiteSpace(s))
                    return;
                list.Add(new FilterWheelDeviceInfo
                {
                    Id = DeviceIdPrefix + s,
                    Name = s,
                    DriverType = DriverType,
                    FilterNames = []
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
                throw new InvalidOperationException($"ASCOM FilterWheel ProgID not found: '{progId}'.");

            _wheel = Activator.CreateInstance(t)
                ?? throw new InvalidOperationException($"Failed to create ASCOM filter wheel: '{progId}'.");

            try
            {
                _wheel.Connected = true;
                _filterNames = ReadFilterNames(_wheel);
            }
            catch
            {
                ReleaseCom(_wheel);
                _wheel = null;
                _filterNames = [];
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

    public async Task SetPositionAsync(int position)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_wheel is null)
                throw new InvalidOperationException("No filter wheel connected.");
            _wheel.Position = position;
        }

        await WaitForPositionAsync(position).ConfigureAwait(false);
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

    private async Task WaitForPositionAsync(int target)
    {
        const int maxWaitMs = 120_000;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < maxWaitMs)
        {
            int pos;
            lock (_sync)
            {
                if (_wheel is null)
                    return;
                try
                {
                    pos = (int)_wheel.Position;
                }
                catch
                {
                    return;
                }
            }

            if (pos == target)
                return;

            await Task.Delay(50).ConfigureAwait(false);
        }

        throw new TimeoutException($"Filter wheel did not reach position {target}.");
    }

    private static List<string> ReadFilterNames(dynamic wheel)
    {
        var names = new List<string>();
        try
        {
            object? n = wheel.Names;
            switch (n)
            {
                case string[] sa:
                    names.AddRange(sa.Select(s => s ?? string.Empty));
                    break;
                case string one:
                    names.Add(one);
                    break;
                case IEnumerable enumerable:
                    foreach (var item in enumerable)
                        names.Add(item?.ToString() ?? string.Empty);
                    break;
            }
        }
        catch
        {
            // optional property
        }

        return names;
    }

    private void DisconnectCore()
    {
        if (_wheel is null)
            return;
        try
        {
            _wheel.Connected = false;
        }
        catch
        {
            // ignore
        }

        ReleaseCom(_wheel);
        _wheel = null;
        _filterNames = [];
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
