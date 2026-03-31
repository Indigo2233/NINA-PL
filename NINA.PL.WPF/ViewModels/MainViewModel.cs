using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NINA.PL.AutoFocus;
using NINA.PL.Capture;
using NINA.PL.Core;
using NINA.PL.Guider;
using NINA.PL.LiveStack;

namespace NINA.PL.WPF.ViewModels;

public enum SidebarSection
{
    Equipment,
    Capture,
    Focus,
    Guider,
    LiveStack,
    Sequencer,
    Settings,
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly CameraMediator _cameraMediator;
    private readonly MountMediator _mountMediator;
    private readonly FocuserMediator _focuserMediator;
    private readonly FilterWheelMediator _filterWheelMediator;
    private readonly CaptureEngine _captureEngine;
    private readonly AutoFocusEngine _autoFocusEngine;
    private readonly PlanetaryGuider _planetaryGuider;
    private readonly LiveStackEngine _liveStackEngine;
    private readonly DispatcherTimer _statusTimer;

    public MainViewModel(
        CameraMediator cameraMediator,
        MountMediator mountMediator,
        FocuserMediator focuserMediator,
        FilterWheelMediator filterWheelMediator,
        CaptureEngine captureEngine,
        AutoFocusEngine autoFocusEngine,
        PlanetaryGuider planetaryGuider,
        LiveStackEngine liveStackEngine,
        EquipmentViewModel equipment,
        CaptureViewModel capture,
        FocusViewModel focus,
        GuiderViewModel guider,
        LiveStackViewModel liveStack,
        SequencerPanelViewModel sequencer,
        SettingsPanelViewModel settings)
    {
        _cameraMediator = cameraMediator;
        _mountMediator = mountMediator;
        _focuserMediator = focuserMediator;
        _filterWheelMediator = filterWheelMediator;
        _captureEngine = captureEngine;
        _autoFocusEngine = autoFocusEngine;
        _planetaryGuider = planetaryGuider;
        _liveStackEngine = liveStackEngine;

        Equipment = equipment;
        Capture = capture;
        Focus = focus;
        Guider = guider;
        LiveStack = liveStack;
        Sequencer = sequencer;
        Settings = settings;

        SelectedSection = SidebarSection.Equipment;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _statusTimer.Tick += (_, _) => RefreshStatusBar();
        _statusTimer.Start();
        RefreshStatusBar();

        CurrentContent = Equipment;
    }

    public EquipmentViewModel Equipment { get; }
    public CaptureViewModel Capture { get; }
    public FocusViewModel Focus { get; }
    public GuiderViewModel Guider { get; }
    public LiveStackViewModel LiveStack { get; }
    public SequencerPanelViewModel Sequencer { get; }
    public SettingsPanelViewModel Settings { get; }

    public CameraMediator CameraMediator => _cameraMediator;
    public MountMediator MountMediator => _mountMediator;
    public FocuserMediator FocuserMediator => _focuserMediator;
    public FilterWheelMediator FilterWheelMediator => _filterWheelMediator;
    public CaptureEngine CaptureEngine => _captureEngine;
    public AutoFocusEngine AutoFocusEngine => _autoFocusEngine;
    public PlanetaryGuider PlanetaryGuider => _planetaryGuider;
    public LiveStackEngine LiveStackEngine => _liveStackEngine;

    [ObservableProperty]
    private SidebarSection selectedSection;

    [ObservableProperty]
    private object currentContent = null!;

    [ObservableProperty]
    private string statusBarText = string.Empty;

    partial void OnSelectedSectionChanged(SidebarSection value)
    {
        CurrentContent = value switch
        {
            SidebarSection.Equipment => Equipment,
            SidebarSection.Capture => Capture,
            SidebarSection.Focus => Focus,
            SidebarSection.Guider => Guider,
            SidebarSection.LiveStack => LiveStack,
            SidebarSection.Sequencer => Sequencer,
            SidebarSection.Settings => Settings,
            _ => Equipment,
        };
    }

    [RelayCommand]
    private void Navigate(SidebarSection section)
    {
        SelectedSection = section;
    }

    [RelayCommand]
    private void Exit()
    {
        Application.Current.Shutdown();
    }

    private void RefreshStatusBar()
    {
        string cam = _cameraMediator.IsConnected
            ? (_cameraMediator.ConnectedDeviceName ?? "Camera")
            : "—";
        string mnt = _mountMediator.IsConnected
            ? (_mountMediator.ConnectedDeviceName ?? "Mount")
            : "—";
        double fps = Capture.CurrentFps;
        string fpsPart = $"{fps:F1} FPS";
        string rec = _captureEngine.IsCapturing ? "● REC" : "Idle";
        StatusBarText =
            $"{cam}  |  {fpsPart}  |  Frames: {Capture.FramesCaptured}  |  Dropped: {Capture.FramesDropped}  |  {rec}  |  Mount: {mnt}";
    }
}
