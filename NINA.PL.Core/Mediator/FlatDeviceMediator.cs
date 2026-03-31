using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NINA.PL.Core;

/// <summary>
/// Aggregates <see cref="IFlatDeviceProvider"/> instances and exposes flat panel operations to the UI.
/// </summary>
public partial class FlatDeviceMediator : ObservableObject, IDisposable
{
    private readonly List<IFlatDeviceProvider> _providers = new();
    private readonly object _providersLock = new();
    private readonly object _connectionLock = new();
    private IFlatDeviceProvider? _connected;
    private bool _disposed;

    [ObservableProperty]
    private bool isConnected;

    [ObservableProperty]
    private string? connectedDeviceId;

    [ObservableProperty]
    private string? connectedDeviceName;

    [ObservableProperty]
    private bool coverIsOpen;

    [ObservableProperty]
    private int brightness;

    [ObservableProperty]
    private int minBrightness;

    [ObservableProperty]
    private int maxBrightness;

    [ObservableProperty]
    private bool lightOn;

    public void RegisterProvider(IFlatDeviceProvider provider)
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

    public async Task<IReadOnlyList<FlatDeviceInfo>> GetAllDevicesAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var result = new List<FlatDeviceInfo>();
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
                Logger.Warn(ex, "Flat device provider {DriverType} enumeration failed.", provider.DriverType);
            }
        }

        return result;
    }

    public async Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        await DisconnectAsync(cancellationToken).ConfigureAwait(false);

        IFlatDeviceProvider? chosen = null;
        FlatDeviceInfo? meta = null;
        foreach (var provider in SnapshotProviders())
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<FlatDeviceInfo> devices;
            try
            {
                devices = await provider.EnumerateAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Flat device provider {DriverType} skipped while resolving {DeviceId}.", provider.DriverType, deviceId);
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
            throw new InvalidOperationException($"No registered flat device provider exposes device id '{deviceId}'.");
        }

        await chosen.ConnectAsync(deviceId).ConfigureAwait(false);
        AttachProvider(chosen, meta);
        RefreshStateFromProvider();
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IFlatDeviceProvider? toRelease;
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
                Logger.Error(ex, "Error disconnecting flat device provider {DriverType}.", toRelease.DriverType);
            }
        }

        ApplyDisconnectedState();
    }

    public void RefreshStateFromProvider()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IFlatDeviceProvider? f;
        lock (_connectionLock)
        {
            f = _connected;
        }

        if (f is null || !f.IsConnected)
        {
            return;
        }

        CoverIsOpen = f.CoverIsOpen;
        Brightness = f.Brightness;
        MinBrightness = f.MinBrightness;
        MaxBrightness = f.MaxBrightness;
        LightOn = f.LightOn;
    }

    public IFlatDeviceProvider? GetConnectedProvider()
    {
        lock (_connectionLock)
        {
            return _connected;
        }
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
            Logger.Error(ex, "FlatDeviceMediator.Dispose: disconnect failed.");
        }

        lock (_providersLock)
        {
            _providers.Clear();
        }

        GC.SuppressFinalize(this);
    }

    private void AttachProvider(IFlatDeviceProvider provider, FlatDeviceInfo meta)
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
        CoverIsOpen = false;
        Brightness = 0;
        MinBrightness = 0;
        MaxBrightness = 0;
        LightOn = false;
    }

    private List<IFlatDeviceProvider> SnapshotProviders()
    {
        lock (_providersLock)
        {
            return new List<IFlatDeviceProvider>(_providers);
        }
    }
}
