using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NINA.PL.Core;

/// <summary>
/// Aggregates <see cref="IFocuserProvider"/> instances and exposes focuser motion to the UI.
/// </summary>
public partial class FocuserMediator : ObservableObject, IDisposable
{
    private readonly List<IFocuserProvider> _providers = new();
    private readonly object _providersLock = new();
    private readonly object _connectionLock = new();
    private IFocuserProvider? _connected;
    private bool _disposed;

    [ObservableProperty]
    private bool isConnected;

    [ObservableProperty]
    private string? connectedDeviceId;

    [ObservableProperty]
    private string? connectedDeviceName;

    [ObservableProperty]
    private int position;

    [ObservableProperty]
    private int maxPosition;

    [ObservableProperty]
    private bool isMoving;

    [ObservableProperty]
    private double temperature;

    public void RegisterProvider(IFocuserProvider provider)
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

    public async Task<IReadOnlyList<FocuserDeviceInfo>> GetAllDevicesAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var result = new List<FocuserDeviceInfo>();
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
                Logger.Warn(ex, "Focuser provider {DriverType} enumeration failed.", provider.DriverType);
            }
        }

        return result;
    }

    public async Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        await DisconnectAsync(cancellationToken).ConfigureAwait(false);

        IFocuserProvider? chosen = null;
        FocuserDeviceInfo? meta = null;
        foreach (var provider in SnapshotProviders())
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<FocuserDeviceInfo> devices;
            try
            {
                devices = await provider.EnumerateAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Focuser provider {DriverType} skipped while resolving {DeviceId}.", provider.DriverType, deviceId);
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
            throw new InvalidOperationException($"No registered focuser provider exposes device id '{deviceId}'.");
        }

        await chosen.ConnectAsync(deviceId).ConfigureAwait(false);
        AttachProvider(chosen, meta);
        RefreshStateFromProvider();
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IFocuserProvider? toRelease;
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
                Logger.Error(ex, "Error disconnecting focuser provider {DriverType}.", toRelease.DriverType);
            }
        }

        ApplyDisconnectedState();
    }

    public void RefreshStateFromProvider()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IFocuserProvider? f;
        lock (_connectionLock)
        {
            f = _connected;
        }

        if (f is null || !f.IsConnected)
        {
            return;
        }

        Position = f.Position;
        MaxPosition = f.MaxPosition;
        IsMoving = f.IsMoving;
        Temperature = f.Temperature;
    }

    public IFocuserProvider? GetConnectedProvider()
    {
        lock (_connectionLock)
        {
            return _connected;
        }
    }

    public async Task MoveAsync(int targetPosition, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var f = GetConnectedOrThrow();
        await f.MoveAsync(targetPosition).ConfigureAwait(false);
        RefreshStateFromProvider();
    }

    public async Task MoveRelativeAsync(int offset, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var f = GetConnectedOrThrow();
        await f.MoveRelativeAsync(offset).ConfigureAwait(false);
        RefreshStateFromProvider();
    }

    public async Task HaltAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var f = GetConnectedOrThrow();
        await f.HaltAsync().ConfigureAwait(false);
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
            Logger.Error(ex, "FocuserMediator.Dispose: disconnect failed.");
        }

        lock (_providersLock)
        {
            _providers.Clear();
        }

        GC.SuppressFinalize(this);
    }

    private IFocuserProvider GetConnectedOrThrow()
    {
        lock (_connectionLock)
        {
            if (_connected is null || !_connected.IsConnected)
            {
                throw new InvalidOperationException("No focuser is connected.");
            }

            return _connected;
        }
    }

    private void AttachProvider(IFocuserProvider provider, FocuserDeviceInfo meta)
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
        Position = 0;
        MaxPosition = 0;
        IsMoving = false;
        Temperature = double.NaN;
    }

    private List<IFocuserProvider> SnapshotProviders()
    {
        lock (_providersLock)
        {
            return new List<IFocuserProvider>(_providers);
        }
    }
}
