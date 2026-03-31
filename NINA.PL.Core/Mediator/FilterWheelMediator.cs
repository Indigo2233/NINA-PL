using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NINA.PL.Core;

/// <summary>
/// Aggregates <see cref="IFilterWheelProvider"/> instances and exposes filter selection to the UI.
/// </summary>
public partial class FilterWheelMediator : ObservableObject, IDisposable
{
    private readonly List<IFilterWheelProvider> _providers = new();
    private readonly object _providersLock = new();
    private readonly object _connectionLock = new();
    private IFilterWheelProvider? _connected;
    private bool _disposed;

    [ObservableProperty]
    private bool isConnected;

    [ObservableProperty]
    private string? connectedDeviceId;

    [ObservableProperty]
    private string? connectedDeviceName;

    [ObservableProperty]
    private int currentPosition;

    [ObservableProperty]
    private IReadOnlyList<string> filterNames = Array.Empty<string>();

    public void RegisterProvider(IFilterWheelProvider provider)
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

    public async Task<IReadOnlyList<FilterWheelDeviceInfo>> GetAllDevicesAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var result = new List<FilterWheelDeviceInfo>();
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
                Logger.Warn(ex, "Filter wheel provider {DriverType} enumeration failed.", provider.DriverType);
            }
        }

        return result;
    }

    public async Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        await DisconnectAsync(cancellationToken).ConfigureAwait(false);

        IFilterWheelProvider? chosen = null;
        FilterWheelDeviceInfo? meta = null;
        foreach (var provider in SnapshotProviders())
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<FilterWheelDeviceInfo> devices;
            try
            {
                devices = await provider.EnumerateAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Filter wheel provider {DriverType} skipped while resolving {DeviceId}.", provider.DriverType, deviceId);
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
            throw new InvalidOperationException($"No registered filter wheel provider exposes device id '{deviceId}'.");
        }

        await chosen.ConnectAsync(deviceId).ConfigureAwait(false);
        AttachProvider(chosen, meta);
        RefreshStateFromProvider();
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IFilterWheelProvider? toRelease;
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
                Logger.Error(ex, "Error disconnecting filter wheel provider {DriverType}.", toRelease.DriverType);
            }
        }

        ApplyDisconnectedState();
    }

    public void RefreshStateFromProvider()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IFilterWheelProvider? w;
        lock (_connectionLock)
        {
            w = _connected;
        }

        if (w is null || !w.IsConnected)
        {
            return;
        }

        CurrentPosition = w.CurrentPosition;
        FilterNames = w.FilterNames.Count == 0
            ? Array.Empty<string>()
            : w.FilterNames.ToArray();
    }

    public IFilterWheelProvider? GetConnectedProvider()
    {
        lock (_connectionLock)
        {
            return _connected;
        }
    }

    public async Task SetPositionAsync(int position, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var w = GetConnectedOrThrow();
        await w.SetPositionAsync(position).ConfigureAwait(false);
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
            Logger.Error(ex, "FilterWheelMediator.Dispose: disconnect failed.");
        }

        lock (_providersLock)
        {
            _providers.Clear();
        }

        GC.SuppressFinalize(this);
    }

    private IFilterWheelProvider GetConnectedOrThrow()
    {
        lock (_connectionLock)
        {
            if (_connected is null || !_connected.IsConnected)
            {
                throw new InvalidOperationException("No filter wheel is connected.");
            }

            return _connected;
        }
    }

    private void AttachProvider(IFilterWheelProvider provider, FilterWheelDeviceInfo meta)
    {
        lock (_connectionLock)
        {
            _connected = provider;
        }

        ConnectedDeviceId = meta.Id;
        ConnectedDeviceName = meta.Name;
        IsConnected = provider.IsConnected;
        FilterNames = meta.FilterNames.Count == 0
            ? Array.Empty<string>()
            : meta.FilterNames.ToArray();
    }

    private void ApplyDisconnectedState()
    {
        IsConnected = false;
        ConnectedDeviceId = null;
        ConnectedDeviceName = null;
        CurrentPosition = 0;
        FilterNames = Array.Empty<string>();
    }

    private List<IFilterWheelProvider> SnapshotProviders()
    {
        lock (_providersLock)
        {
            return new List<IFilterWheelProvider>(_providers);
        }
    }
}
