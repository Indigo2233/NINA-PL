using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NINA.PL.Core;

namespace NINA.PL.WPF.ViewModels;

public sealed class FilterSlotRow
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsCurrent { get; init; }
}

public sealed partial class EquipmentViewModel : ObservableObject
{
    private readonly CameraMediator _camera;
    private readonly MountMediator _mount;
    private readonly FocuserMediator _focuser;
    private readonly FilterWheelMediator _filterWheel;
    private readonly DispatcherTimer _telemetryTimer;

    public EquipmentViewModel(
        CameraMediator camera,
        MountMediator mount,
        FocuserMediator focuser,
        FilterWheelMediator filterWheel)
    {
        _camera = camera;
        _mount = mount;
        _focuser = focuser;
        _filterWheel = filterWheel;

        _camera.PropertyChanged += OnCameraMediatorPropertyChanged;
        _mount.PropertyChanged += OnMountMediatorPropertyChanged;
        _focuser.PropertyChanged += OnFocuserMediatorPropertyChanged;
        _filterWheel.PropertyChanged += OnFilterWheelMediatorPropertyChanged;

        SyncAllFromMediators();
        RefreshTelemetry();

        _telemetryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _telemetryTimer.Tick += (_, _) => RefreshTelemetry();
        _telemetryTimer.Start();
    }

    [ObservableProperty]
    private ObservableCollection<CameraDeviceInfo> availableCameras = new();

    [ObservableProperty]
    private CameraDeviceInfo? selectedCamera;

    [ObservableProperty]
    private bool isCameraConnected;

    [ObservableProperty]
    private string cameraStatus = "Disconnected";

    [ObservableProperty]
    private string cameraSensorSummary = "—";

    [ObservableProperty]
    private string cameraFormatsSummary = "—";

    [ObservableProperty]
    private ObservableCollection<MountDeviceInfo> availableMounts = new();

    [ObservableProperty]
    private MountDeviceInfo? selectedMount;

    [ObservableProperty]
    private bool isMountConnected;

    [ObservableProperty]
    private string mountStatus = "Disconnected";

    [ObservableProperty]
    private double mountRightAscensionHours;

    [ObservableProperty]
    private double mountDeclinationDegrees;

    [ObservableProperty]
    private double mountAltitudeDegrees;

    [ObservableProperty]
    private double mountAzimuthDegrees;

    [ObservableProperty]
    private bool mountIsTracking;

    [ObservableProperty]
    private ObservableCollection<FocuserDeviceInfo> availableFocusers = new();

    [ObservableProperty]
    private FocuserDeviceInfo? selectedFocuser;

    [ObservableProperty]
    private bool isFocuserConnected;

    [ObservableProperty]
    private string focuserStatus = "Disconnected";

    [ObservableProperty]
    private int focuserPosition;

    [ObservableProperty]
    private int focuserMaxPosition;

    [ObservableProperty]
    private double focuserTemperature = double.NaN;

    [ObservableProperty]
    private bool focuserIsMoving;

    [ObservableProperty]
    private int focuserTargetPosition;

    [ObservableProperty]
    private ObservableCollection<FilterWheelDeviceInfo> availableFilterWheels = new();

    [ObservableProperty]
    private FilterWheelDeviceInfo? selectedFilterWheel;

    [ObservableProperty]
    private bool isFilterWheelConnected;

    [ObservableProperty]
    private string filterWheelStatus = "Disconnected";

    [ObservableProperty]
    private string currentFilterName = "—";

    [ObservableProperty]
    private ObservableCollection<FilterSlotRow> filterSlots = new();

    private void OnCameraMediatorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CameraMediator.IsConnected)
            or nameof(CameraMediator.ConnectedDeviceName)
            or nameof(CameraMediator.ConnectedDeviceId))
        {
            SyncCameraFromMediator();
            RefreshTelemetry();
        }
    }

    private void OnMountMediatorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MountMediator.IsConnected)
            or nameof(MountMediator.ConnectedDeviceName)
            or nameof(MountMediator.ConnectedDeviceId))
        {
            SyncMountFromMediator();
        }

        SyncMountFromProviderProps();
    }

    private void OnFocuserMediatorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FocuserMediator.IsConnected)
            or nameof(FocuserMediator.ConnectedDeviceName)
            or nameof(FocuserMediator.ConnectedDeviceId))
        {
            SyncFocuserFromMediator();
        }

        SyncFocuserFromProviderProps();
    }

    private void OnFilterWheelMediatorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FilterWheelMediator.IsConnected)
            or nameof(FilterWheelMediator.ConnectedDeviceName)
            or nameof(FilterWheelMediator.ConnectedDeviceId))
        {
            SyncFilterWheelFromMediator();
        }

        RebuildFilterSlots();
    }

    private void SyncAllFromMediators()
    {
        SyncCameraFromMediator();
        SyncMountFromMediator();
        SyncFocuserFromMediator();
        SyncFilterWheelFromMediator();
    }

    private void RefreshTelemetry()
    {
        if (_mount.IsConnected)
            _mount.RefreshStateFromProvider();
        if (_focuser.IsConnected)
            _focuser.RefreshStateFromProvider();
        if (_filterWheel.IsConnected)
            _filterWheel.RefreshStateFromProvider();

        SyncCameraTelemetry();
        SyncMountFromProviderProps();
        SyncFocuserFromProviderProps();
        RebuildFilterSlots();
    }

    private void SyncCameraFromMediator()
    {
        IsCameraConnected = _camera.IsConnected;
        CameraStatus = _camera.IsConnected
            ? $"Connected: {_camera.ConnectedDeviceName} ({_camera.ConnectedDeviceId})"
            : "Disconnected";
    }

    private void SyncCameraTelemetry()
    {
        ICameraProvider? cam = _camera.GetConnectedProvider();
        if (cam is null || !cam.IsConnected)
        {
            CameraSensorSummary = "—";
            CameraFormatsSummary = "—";
            return;
        }

        string color = cam.IsColor ? "Color" : "Mono";
        CameraSensorSummary =
            $"{cam.ModelName} · {cam.SensorWidth}×{cam.SensorHeight} px · {cam.PixelSizeUm:F2} µm · {color} · Bayer: {cam.BayerPattern}";
        try
        {
            IReadOnlyList<string> formats = cam.GetPixelFormats();
            CameraFormatsSummary = formats.Count == 0 ? "—" : string.Join(", ", formats);
        }
        catch
        {
            CameraFormatsSummary = "—";
        }
    }

    private void SyncMountFromMediator()
    {
        IsMountConnected = _mount.IsConnected;
        MountStatus = _mount.IsConnected
            ? $"Connected: {_mount.ConnectedDeviceName} ({_mount.ConnectedDeviceId})"
            : "Disconnected";
        SyncMountFromProviderProps();
    }

    private void SyncMountFromProviderProps()
    {
        if (!_mount.IsConnected)
        {
            MountRightAscensionHours = 0;
            MountDeclinationDegrees = 0;
            MountAltitudeDegrees = 0;
            MountAzimuthDegrees = 0;
            MountIsTracking = false;
            return;
        }

        MountRightAscensionHours = _mount.RightAscension;
        MountDeclinationDegrees = _mount.Declination;
        MountAltitudeDegrees = _mount.Altitude;
        MountAzimuthDegrees = _mount.Azimuth;
        MountIsTracking = _mount.IsTracking;
    }

    private void SyncFocuserFromMediator()
    {
        IsFocuserConnected = _focuser.IsConnected;
        FocuserStatus = _focuser.IsConnected
            ? $"Connected: {_focuser.ConnectedDeviceName} ({_focuser.ConnectedDeviceId})"
            : "Disconnected";
        SyncFocuserFromProviderProps();
    }

    private void SyncFocuserFromProviderProps()
    {
        if (!_focuser.IsConnected)
        {
            FocuserPosition = 0;
            FocuserMaxPosition = 0;
            FocuserTemperature = double.NaN;
            FocuserIsMoving = false;
            return;
        }

        FocuserPosition = _focuser.Position;
        FocuserMaxPosition = _focuser.MaxPosition;
        FocuserTemperature = _focuser.Temperature;
        FocuserIsMoving = _focuser.IsMoving;
    }

    private void SyncFilterWheelFromMediator()
    {
        IsFilterWheelConnected = _filterWheel.IsConnected;
        FilterWheelStatus = _filterWheel.IsConnected
            ? $"Connected: {_filterWheel.ConnectedDeviceName} ({_filterWheel.ConnectedDeviceId})"
            : "Disconnected";
        RebuildFilterSlots();
    }

    private void RebuildFilterSlots()
    {
        if (!_filterWheel.IsConnected)
        {
            CurrentFilterName = "—";
            FilterSlots = new ObservableCollection<FilterSlotRow>();
            return;
        }

        int pos = _filterWheel.CurrentPosition;
        IReadOnlyList<string> names = _filterWheel.FilterNames;
        string nameAt = pos >= 0 && pos < names.Count ? names[pos] : $"Slot {pos}";
        CurrentFilterName = nameAt;

        var rows = new List<FilterSlotRow>();
        for (int i = 0; i < names.Count; i++)
        {
            rows.Add(new FilterSlotRow
            {
                Index = i,
                Name = string.IsNullOrWhiteSpace(names[i]) ? $"Filter {i}" : names[i],
                IsCurrent = i == pos,
            });
        }

        FilterSlots = new ObservableCollection<FilterSlotRow>(rows);
    }

    [RelayCommand]
    private async Task RefreshCamerasAsync()
    {
        var list = await _camera.GetAllDevicesAsync().ConfigureAwait(true);
        AvailableCameras = new ObservableCollection<CameraDeviceInfo>(list);
        if (SelectedCamera is null && AvailableCameras.Count > 0)
            SelectedCamera = AvailableCameras[0];
    }

    [RelayCommand]
    private async Task ConnectCameraAsync()
    {
        if (SelectedCamera is null)
            return;
        await _camera.ConnectAsync(SelectedCamera.Id).ConfigureAwait(true);
        SyncCameraFromMediator();
        RefreshTelemetry();
    }

    [RelayCommand]
    private async Task DisconnectCameraAsync()
    {
        await _camera.DisconnectAsync().ConfigureAwait(true);
        SyncCameraFromMediator();
        RefreshTelemetry();
    }

    [RelayCommand]
    private async Task ToggleCameraConnectionAsync()
    {
        if (IsCameraConnected)
            await DisconnectCameraAsync().ConfigureAwait(true);
        else
            await ConnectCameraAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void OpenCameraSettings()
    {
        // Reserved for driver-specific settings UI.
    }

    [RelayCommand]
    private async Task RefreshMountsAsync()
    {
        var list = await _mount.GetAllDevicesAsync().ConfigureAwait(true);
        AvailableMounts = new ObservableCollection<MountDeviceInfo>(list);
        if (SelectedMount is null && AvailableMounts.Count > 0)
            SelectedMount = AvailableMounts[0];
    }

    [RelayCommand]
    private async Task ConnectMountAsync()
    {
        if (SelectedMount is null)
            return;
        await _mount.ConnectAsync(SelectedMount.Id).ConfigureAwait(true);
        SyncMountFromMediator();
    }

    [RelayCommand]
    private async Task DisconnectMountAsync()
    {
        await _mount.DisconnectAsync().ConfigureAwait(true);
        SyncMountFromMediator();
    }

    [RelayCommand]
    private async Task ToggleMountConnectionAsync()
    {
        if (IsMountConnected)
            await DisconnectMountAsync().ConfigureAwait(true);
        else
            await ConnectMountAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void OpenMountSettings()
    {
    }

    [RelayCommand]
    private async Task RefreshFocusersAsync()
    {
        var list = await _focuser.GetAllDevicesAsync().ConfigureAwait(true);
        AvailableFocusers = new ObservableCollection<FocuserDeviceInfo>(list);
        if (SelectedFocuser is null && AvailableFocusers.Count > 0)
            SelectedFocuser = AvailableFocusers[0];
    }

    [RelayCommand]
    private async Task ConnectFocuserAsync()
    {
        if (SelectedFocuser is null)
            return;
        await _focuser.ConnectAsync(SelectedFocuser.Id).ConfigureAwait(true);
        SyncFocuserFromMediator();
    }

    [RelayCommand]
    private async Task DisconnectFocuserAsync()
    {
        await _focuser.DisconnectAsync().ConfigureAwait(true);
        SyncFocuserFromMediator();
    }

    [RelayCommand]
    private async Task ToggleFocuserConnectionAsync()
    {
        if (IsFocuserConnected)
            await DisconnectFocuserAsync().ConfigureAwait(true);
        else
            await ConnectFocuserAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void OpenFocuserSettings()
    {
    }

    [RelayCommand]
    private async Task FocuserJogAsync(string? deltaStr)
    {
        if (!int.TryParse(deltaStr, out int delta))
            return;
        IFocuserProvider? f = _focuser.GetConnectedProvider();
        if (f is null || !f.IsConnected)
            return;
        try
        {
            await f.MoveRelativeAsync(delta).ConfigureAwait(true);
        }
        catch
        {
            // UI stays consistent on next telemetry tick.
        }
    }

    [RelayCommand]
    private async Task MoveFocuserToTargetAsync()
    {
        IFocuserProvider? f = _focuser.GetConnectedProvider();
        if (f is null || !f.IsConnected)
            return;
        int t = Math.Clamp(FocuserTargetPosition, 0, f.MaxPosition);
        try
        {
            await f.MoveAsync(t).ConfigureAwait(true);
        }
        catch
        {
        }
    }

    [RelayCommand]
    private async Task RefreshFilterWheelsAsync()
    {
        var list = await _filterWheel.GetAllDevicesAsync().ConfigureAwait(true);
        AvailableFilterWheels = new ObservableCollection<FilterWheelDeviceInfo>(list);
        if (SelectedFilterWheel is null && AvailableFilterWheels.Count > 0)
            SelectedFilterWheel = AvailableFilterWheels[0];
    }

    [RelayCommand]
    private async Task ConnectFilterWheelAsync()
    {
        if (SelectedFilterWheel is null)
            return;
        await _filterWheel.ConnectAsync(SelectedFilterWheel.Id).ConfigureAwait(true);
        SyncFilterWheelFromMediator();
    }

    [RelayCommand]
    private async Task DisconnectFilterWheelAsync()
    {
        await _filterWheel.DisconnectAsync().ConfigureAwait(true);
        SyncFilterWheelFromMediator();
    }

    [RelayCommand]
    private async Task ToggleFilterWheelConnectionAsync()
    {
        if (IsFilterWheelConnected)
            await DisconnectFilterWheelAsync().ConfigureAwait(true);
        else
            await ConnectFilterWheelAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void OpenFilterWheelSettings()
    {
    }

    [RelayCommand]
    private async Task SelectFilterSlotAsync(int index)
    {
        IFilterWheelProvider? w = _filterWheel.GetConnectedProvider();
        if (w is null || !w.IsConnected)
            return;
        try
        {
            await w.SetPositionAsync(index).ConfigureAwait(true);
        }
        catch
        {
        }

        RebuildFilterSlots();
    }

    [RelayCommand]
    private async Task RefreshAllAsync()
    {
        await RefreshCamerasAsync().ConfigureAwait(true);
        await RefreshMountsAsync().ConfigureAwait(true);
        await RefreshFocusersAsync().ConfigureAwait(true);
        await RefreshFilterWheelsAsync().ConfigureAwait(true);
    }
}
