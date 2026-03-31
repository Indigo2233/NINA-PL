using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NINA.PL.Core;

/// <summary>
/// Aggregates <see cref="ICameraProvider"/> instances, exposes discovery/connection to the UI,
/// and forwards <see cref="FrameData"/> from the active camera.
/// </summary>
public partial class CameraMediator : ObservableObject, IDisposable
{
    private readonly List<ICameraProvider> _providers = new();
    private readonly object _providersLock = new();
    private readonly object _connectionLock = new();
    private ICameraProvider? _connected;
    private bool _disposed;

    [ObservableProperty]
    private bool isConnected;

    [ObservableProperty]
    private string? connectedDeviceId;

    [ObservableProperty]
    private string? connectedDeviceName;

    /// <summary>Raised when the connected provider delivers a frame.</summary>
    public event EventHandler<FrameData>? FrameReceived;

    /// <summary>Registers a camera backend. Duplicate references are ignored.</summary>
    public void RegisterProvider(ICameraProvider provider)
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

    /// <summary>
    /// Enumerates devices from all registered providers. Providers that fail enumeration are skipped.
    /// </summary>
    public async Task<IReadOnlyList<CameraDeviceInfo>> GetAllDevicesAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var result = new List<CameraDeviceInfo>();
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
                Logger.Warn(ex, "Camera provider '{DriverType}' enumeration failed.", provider.DriverType);
            }
        }

        return result;
    }

    /// <summary>
    /// Connects to the first provider that advertises <paramref name="deviceId"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">No provider owns the device id.</exception>
    public async Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        await DisconnectAsync(cancellationToken).ConfigureAwait(false);

        ICameraProvider? chosen = null;
        CameraDeviceInfo? meta = null;
        foreach (var provider in SnapshotProviders())
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<CameraDeviceInfo> devices;
            try
            {
                devices = await provider.EnumerateAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Camera provider '{DriverType}' skipped while resolving '{DeviceId}'.", provider.DriverType, deviceId);
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
            throw new InvalidOperationException($"No registered camera provider exposes device id '{deviceId}'.");
        }

        await chosen.ConnectAsync(deviceId).ConfigureAwait(false);
        AttachProvider(chosen, meta);
    }

    /// <summary>Disconnects the active camera, if any.</summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ICameraProvider? toRelease;
        lock (_connectionLock)
        {
            toRelease = _connected;
            _connected = null;
        }

        if (toRelease is not null)
        {
            toRelease.FrameReceived -= OnProviderFrameReceived;
            try
            {
                await toRelease.DisconnectAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error disconnecting camera provider '{DriverType}'.", toRelease.DriverType);
            }
        }

        ApplyDisconnectedState();
    }

    /// <summary>Returns the currently connected provider, or <see langword="null"/>.</summary>
    public ICameraProvider? GetConnectedProvider()
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
            Logger.Error(ex, "CameraMediator.Dispose: disconnect failed.");
        }

        lock (_providersLock)
        {
            _providers.Clear();
        }

        GC.SuppressFinalize(this);
    }

    private void AttachProvider(ICameraProvider provider, CameraDeviceInfo meta)
    {
        lock (_connectionLock)
        {
            _connected = provider;
        }

        provider.FrameReceived += OnProviderFrameReceived;
        ConnectedDeviceId = meta.Id;
        ConnectedDeviceName = meta.Name;
        IsConnected = provider.IsConnected;
    }

    private void ApplyDisconnectedState()
    {
        IsConnected = false;
        ConnectedDeviceId = null;
        ConnectedDeviceName = null;
    }

    private void OnProviderFrameReceived(object? sender, FrameData e)
    {
        FrameReceived?.Invoke(this, e);
    }

    private List<ICameraProvider> SnapshotProviders()
    {
        lock (_providersLock)
        {
            return new List<ICameraProvider>(_providers);
        }
    }
}
