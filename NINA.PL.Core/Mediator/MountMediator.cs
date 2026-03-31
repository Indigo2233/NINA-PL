using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NINA.PL.Core;

/// <summary>
/// Aggregates <see cref="IMountProvider"/> instances and exposes mount operations to the UI.
/// </summary>
public partial class MountMediator : ObservableObject, IDisposable
{
    private readonly List<IMountProvider> _providers = new();
    private readonly object _providersLock = new();
    private readonly object _connectionLock = new();
    private IMountProvider? _connected;
    private bool _disposed;

    [ObservableProperty]
    private bool isConnected;

    [ObservableProperty]
    private string? connectedDeviceId;

    [ObservableProperty]
    private string? connectedDeviceName;

    [ObservableProperty]
    private bool isTracking;

    [ObservableProperty]
    private double rightAscension;

    [ObservableProperty]
    private double declination;

    [ObservableProperty]
    private double altitude;

    [ObservableProperty]
    private double azimuth;

    [ObservableProperty]
    private bool canPulseGuide;

    public void RegisterProvider(IMountProvider provider)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(provider);
        lock (_providersLock)
        {
            if (!_providers.Contains(provider))
            {
                _providers.Add(provider);
            }
        }
    }

    public async Task<IReadOnlyList<MountDeviceInfo>> GetAllDevicesAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var result = new List<MountDeviceInfo>();
        foreach (var provider in SnapshotProviders())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var batch = await provider.EnumerateAsync().ConfigureAwait(false);
                result.AddRange(batch);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Mount provider {DriverType} enumeration failed.", provider.DriverType);
            }
        }

        return result;
    }

    public async Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        await DisconnectAsync(cancellationToken).ConfigureAwait(false);

        IMountProvider? chosen = null;
        MountDeviceInfo? meta = null;
        foreach (var provider in SnapshotProviders())
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<MountDeviceInfo> devices;
            try
            {
                devices = await provider.EnumerateAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Mount provider {DriverType} skipped while resolving {DeviceId}.", provider.DriverType, deviceId);
                continue;
            }

            foreach (var d in devices)
            {
                if (string.Equals(d.Id, deviceId, StringComparison.Ordinal))
                {
                    chosen = provider;
                    meta = d;
                    break;
                }
            }

            if (chosen is not null)
            {
                break;
            }
        }

        if (chosen is null || meta is null)
        {
            throw new InvalidOperationException($"No registered mount provider exposes device id '{deviceId}'.");
        }

        await chosen.ConnectAsync(deviceId).ConfigureAwait(false);
        AttachProvider(chosen, meta);
        RefreshStateFromProvider();
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IMountProvider? toRelease;
        lock (_connectionLock)
        {
            toRelease = _connected;
            _connected = null;
        }

        if (toRelease is not null)
        {
            try
            {
                await toRelease.DisconnectAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error disconnecting mount provider {DriverType}.", toRelease.DriverType);
            }
        }

        ApplyDisconnectedState();
    }

    /// <summary>Copies coordinates and capability flags from the connected driver.</summary>
    public void RefreshStateFromProvider()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IMountProvider? m;
        lock (_connectionLock)
        {
            m = _connected;
        }

        if (m is null || !m.IsConnected)
        {
            return;
        }

        IsTracking = m.IsTracking;
        RightAscension = m.RightAscension;
        Declination = m.Declination;
        Altitude = m.Altitude;
        Azimuth = m.Azimuth;
        CanPulseGuide = m.CanPulseGuide;
    }

    public IMountProvider? GetConnectedProvider()
    {
        lock (_connectionLock)
        {
            return _connected;
        }
    }

    public async Task SlewToCoordinatesAsync(double raHours, double decDegrees, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var m = GetConnectedOrThrow();
        await m.SlewToCoordinatesAsync(raHours, decDegrees).ConfigureAwait(false);
        RefreshStateFromProvider();
    }

    public async Task PulseGuideAsync(GuideDirection direction, int durationMs, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var m = GetConnectedOrThrow();
        await m.PulseGuideAsync(direction, durationMs).ConfigureAwait(false);
    }

    public async Task SetTrackingAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var m = GetConnectedOrThrow();
        await m.SetTrackingAsync(enabled).ConfigureAwait(false);
        RefreshStateFromProvider();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            DisconnectAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "MountMediator.Dispose: disconnect failed.");
        }

        lock (_providersLock)
        {
            _providers.Clear();
        }

        GC.SuppressFinalize(this);
    }

    private IMountProvider GetConnectedOrThrow()
    {
        lock (_connectionLock)
        {
            if (_connected is null || !_connected.IsConnected)
            {
                throw new InvalidOperationException("No mount is connected.");
            }

            return _connected;
        }
    }

    private void AttachProvider(IMountProvider provider, MountDeviceInfo meta)
    {
        lock (_connectionLock)
        {
            _connected = provider;
        }

        ConnectedDeviceId = meta.Id;
        ConnectedDeviceName = meta.Name;
        IsConnected = provider.IsConnected;
    }

    private void ApplyDisconnectedState()
    {
        IsConnected = false;
        ConnectedDeviceId = null;
        ConnectedDeviceName = null;
        IsTracking = false;
        RightAscension = 0;
        Declination = 0;
        Altitude = 0;
        Azimuth = 0;
        CanPulseGuide = false;
    }

    private List<IMountProvider> SnapshotProviders()
    {
        lock (_providersLock)
        {
            return new List<IMountProvider>(_providers);
        }
    }
}
