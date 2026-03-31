using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NINA.PL.Core;
using NINA.PL.Guider;
using NINA.PL.WPF.Helpers;

namespace NINA.PL.WPF.ViewModels;

public sealed partial class GuiderViewModel : ObservableObject, IDisposable
{
    public IReadOnlyList<TrackingMode> TrackingModes { get; } = Enum.GetValues<TrackingMode>().ToArray();

    private readonly PlanetaryGuider _guider;
    private readonly CameraMediator _camera;
    private readonly MountMediator _mount;
    private readonly Application _app;

    public GuiderViewModel(PlanetaryGuider guider, CameraMediator camera, MountMediator mount)
    {
        _guider = guider;
        _camera = camera;
        _mount = mount;
        _app = Application.Current;

        _guider.GuidingStep += OnGuidingStep;
        _guider.GuidingStarted += OnGuidingStarted;
        _guider.GuidingStopped += OnGuidingStopped;
        _camera.FrameReceived += OnCameraFrame;

        GuideHistory.CollectionChanged += OnGuideHistoryChanged;

        IsCalibrated = _guider.Calibration.IsCalibrated;
        UpdateGuiderStatus();
        RebuildGraphPolylines();
    }

    [ObservableProperty]
    private bool isGuiding;

    [ObservableProperty]
    private bool isCalibrating;

    [ObservableProperty]
    private bool isCalibrated;

    [ObservableProperty]
    private double rmsRa;

    [ObservableProperty]
    private double rmsDec;

    [ObservableProperty]
    private double rmsTotal;

    public ObservableCollection<GuideStep> GuideHistory { get; } = new();

    [ObservableProperty]
    private TrackingMode selectedTrackingMode = TrackingMode.DiskCentroid;

    [ObservableProperty]
    private string guiderMessage = string.Empty;

    [ObservableProperty]
    private string guiderStatusText = "Idle";

    [ObservableProperty]
    private WriteableBitmap? guidePreviewImage;

    [ObservableProperty]
    private PointCollection raGraphPoints = new();

    [ObservableProperty]
    private PointCollection decGraphPoints = new();

    private void OnGuideHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _app.Dispatcher.BeginInvoke(RebuildGraphPolylines);
    }

    private void OnCameraFrame(object? sender, FrameData frame)
    {
        _app.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                GuidePreviewImage = MatBitmapHelper.FrameToWriteableBitmap(frame);
            }
            catch
            {
                // ignore preview failures
            }
        });
    }

    private void RebuildGraphPolylines()
    {
        const double w = 400;
        const double h = 160;
        const int maxPts = 180;

        var raPts = new PointCollection();
        var decPts = new PointCollection();
        int n = GuideHistory.Count;
        if (n == 0)
        {
            RaGraphPoints = raPts;
            DecGraphPoints = decPts;
            return;
        }

        int start = Math.Max(0, n - maxPts);
        var slice = GuideHistory.Skip(start).ToList();
        double maxErr = 0.001;
        foreach (GuideStep g in slice)
        {
            maxErr = Math.Max(maxErr, Math.Abs(g.RaErrorArcSec));
            maxErr = Math.Max(maxErr, Math.Abs(g.DecErrorArcSec));
        }

        for (int i = 0; i < slice.Count; i++)
        {
            double x = slice.Count <= 1 ? 0 : i / (double)(slice.Count - 1) * w;
            double raY = h / 2 - (slice[i].RaErrorArcSec / maxErr) * (h / 2 - 6);
            double decY = h / 2 - (slice[i].DecErrorArcSec / maxErr) * (h / 2 - 6);
            raPts.Add(new System.Windows.Point(x, Math.Clamp(raY, 2, h - 2)));
            decPts.Add(new System.Windows.Point(x, Math.Clamp(decY, 2, h - 2)));
        }

        RaGraphPoints = raPts;
        DecGraphPoints = decPts;
    }

    private void OnGuidingStep(object? sender, GuideStep step)
    {
        GuideHistory.Add(step);
        while (GuideHistory.Count > 200)
            GuideHistory.RemoveAt(0);

        RmsTotal = _guider.RmsTotal;
        RecomputeAxisRms();
    }

    private void RecomputeAxisRms()
    {
        if (GuideHistory.Count == 0)
        {
            RmsRa = 0;
            RmsDec = 0;
            return;
        }

        double sRa = 0;
        double sDec = 0;
        foreach (GuideStep g in GuideHistory)
        {
            sRa += g.RaErrorArcSec * g.RaErrorArcSec;
            sDec += g.DecErrorArcSec * g.DecErrorArcSec;
        }

        int n = GuideHistory.Count;
        RmsRa = Math.Sqrt(sRa / n);
        RmsDec = Math.Sqrt(sDec / n);
    }

    private void OnGuidingStarted(object? sender, EventArgs e)
    {
        IsGuiding = true;
        GuiderMessage = "Guiding active.";
        UpdateGuiderStatus();
    }

    private void OnGuidingStopped(object? sender, EventArgs e)
    {
        IsGuiding = false;
        GuiderMessage = "Guiding stopped.";
        UpdateGuiderStatus();
    }

    private void UpdateGuiderStatus()
    {
        if (IsCalibrating)
            GuiderStatusText = "Calibrating";
        else if (IsGuiding)
            GuiderStatusText = "Guiding";
        else
            GuiderStatusText = "Idle";
    }

    [RelayCommand]
    private void SetTrackingMode(TrackingMode mode)
    {
        SelectedTrackingMode = mode;
    }

    [RelayCommand]
    private async Task StartGuidingAsync()
    {
        try
        {
            GuiderMessage = "Starting guiding…";
            await _guider.StartGuidingAsync(_camera, _mount, SelectedTrackingMode, CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            GuiderMessage = ex.Message;
        }
        finally
        {
            IsGuiding = _guider.IsGuiding;
            UpdateGuiderStatus();
        }
    }

    [RelayCommand]
    private void StopGuiding()
    {
        _guider.StopGuiding();
        UpdateGuiderStatus();
    }

    [RelayCommand]
    private async Task CalibrateAsync()
    {
        IMountProvider? m = _mount.GetConnectedProvider();
        ICameraProvider? c = _camera.GetConnectedProvider();
        if (m is null || c is null)
        {
            GuiderMessage = "Connect camera and mount first.";
            return;
        }

        IsCalibrating = true;
        UpdateGuiderStatus();
        try
        {
            GuiderMessage = "Calibrating guider…";
            await _guider.Calibration.CalibrateAsync(m, c).ConfigureAwait(true);
            IsCalibrated = _guider.Calibration.IsCalibrated;
            GuiderMessage = IsCalibrated ? "Calibration complete." : "Calibration failed.";
        }
        catch (Exception ex)
        {
            GuiderMessage = ex.Message;
            IsCalibrated = _guider.Calibration.IsCalibrated;
        }
        finally
        {
            IsCalibrating = false;
            UpdateGuiderStatus();
        }
    }

    public void Dispose()
    {
        _guider.GuidingStep -= OnGuidingStep;
        _guider.GuidingStarted -= OnGuidingStarted;
        _guider.GuidingStopped -= OnGuidingStopped;
        _camera.FrameReceived -= OnCameraFrame;
        GuideHistory.CollectionChanged -= OnGuideHistoryChanged;
        GC.SuppressFinalize(this);
    }
}
