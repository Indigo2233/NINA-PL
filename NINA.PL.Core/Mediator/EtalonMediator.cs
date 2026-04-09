using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NINA.PL.Core;

/// <summary>
/// Aggregates <see cref="IEtalonProvider"/> instances and exposes etalon tuner
/// motion / auto-tune to the UI and sequencer.
/// </summary>
public partial class EtalonMediator : ObservableObject, IDisposable
{
    private readonly List<IEtalonProvider> _providers = new();
    private readonly object _providersLock = new();
    private readonly object _connectionLock = new();
    private IEtalonProvider? _connected;
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

    public void RegisterProvider(IEtalonProvider provider)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(provider);
        lock (_providersLock)
        {
            if (!_providers.Contains(provider))
                _providers.Add(provider);
        }
    }

    public async Task<IReadOnlyList<EtalonDeviceInfo>> GetAllDevicesAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var result = new List<EtalonDeviceInfo>();
        foreach (var p in SnapshotProviders())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                result.AddRange(await p.EnumerateAsync().ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Etalon provider {DriverType} enumeration failed.", p.DriverType);
            }
        }
        return result;
    }

    public async Task ConnectAsync(string deviceId, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        await DisconnectAsync(ct).ConfigureAwait(false);

        IEtalonProvider? chosen = null;
        EtalonDeviceInfo? meta = null;
        foreach (var p in SnapshotProviders())
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<EtalonDeviceInfo> devices;
            try { devices = await p.EnumerateAsync().ConfigureAwait(false); }
            catch { continue; }

            foreach (var d in devices)
            {
                if (string.Equals(d.Id, deviceId, StringComparison.Ordinal))
                {
                    chosen = p;
                    meta = d;
                    break;
                }
            }
            if (chosen is not null) break;
        }

        if (chosen is null || meta is null)
            throw new InvalidOperationException($"No registered etalon provider exposes device id '{deviceId}'.");

        await chosen.ConnectAsync(deviceId).ConfigureAwait(false);
        lock (_connectionLock) { _connected = chosen; }
        ConnectedDeviceId = meta.Id;
        ConnectedDeviceName = meta.Name;
        IsConnected = chosen.IsConnected;
        RefreshStateFromProvider();
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IEtalonProvider? toRelease;
        lock (_connectionLock) { toRelease = _connected; _connected = null; }
        if (toRelease is not null)
        {
            try { await toRelease.DisconnectAsync().ConfigureAwait(false); }
            catch (Exception ex) { Logger.Error(ex, "Error disconnecting etalon."); }
        }
        ApplyDisconnectedState();
    }

    public void RefreshStateFromProvider()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IEtalonProvider? e;
        lock (_connectionLock) { e = _connected; }
        if (e is null || !e.IsConnected) return;
        Position = e.Position;
        MaxPosition = e.MaxPosition;
        IsMoving = e.IsMoving;
        Temperature = e.Temperature;
    }

    public IEtalonProvider? GetConnectedProvider()
    {
        lock (_connectionLock) { return _connected; }
    }

    public async Task MoveAsync(int targetPosition, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();
        var e = GetConnectedOrThrow();
        await e.MoveAsync(targetPosition).ConfigureAwait(false);
        RefreshStateFromProvider();
    }

    public async Task MoveRelativeAsync(int offset, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();
        var e = GetConnectedOrThrow();
        await e.MoveRelativeAsync(offset).ConfigureAwait(false);
        RefreshStateFromProvider();
    }

    public async Task HaltAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();
        var e = GetConnectedOrThrow();
        await e.HaltAsync().ConfigureAwait(false);
        RefreshStateFromProvider();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { DisconnectAsync(CancellationToken.None).GetAwaiter().GetResult(); }
        catch (Exception ex) { Logger.Error(ex, "EtalonMediator.Dispose: disconnect failed."); }
        lock (_providersLock) { _providers.Clear(); }
        GC.SuppressFinalize(this);
    }

    private IEtalonProvider GetConnectedOrThrow()
    {
        lock (_connectionLock)
        {
            if (_connected is null || !_connected.IsConnected)
                throw new InvalidOperationException("No etalon is connected.");
            return _connected;
        }
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

    private List<IEtalonProvider> SnapshotProviders()
    {
        lock (_providersLock) { return new List<IEtalonProvider>(_providers); }
    }
}
