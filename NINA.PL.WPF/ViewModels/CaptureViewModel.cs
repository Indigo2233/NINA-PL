using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NINA.PL.Capture;
using NINA.PL.Core;
using NINA.PL.Image;
using NINA.PL.WPF.Helpers;
using OpenCvSharp;

namespace NINA.PL.WPF.ViewModels;

public sealed partial class CaptureViewModel : ObservableObject, IDisposable
{
    public IReadOnlyList<CaptureFormat> CaptureFormats { get; } = Enum.GetValues<CaptureFormat>().ToArray();

    private readonly CameraMediator _camera;
    private readonly FilterWheelMediator _filterWheel;
    private readonly CaptureEngine _capture;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _statsTimer;
    private bool _exposureSliderDrive;
    private int _lastFramePayloadBytes;

    public CaptureViewModel(CameraMediator camera, FilterWheelMediator filterWheel, CaptureEngine capture)
    {
        _camera = camera;
        _filterWheel = filterWheel;
        _capture = capture;
        _dispatcher = Application.Current.Dispatcher;

        _camera.FrameReceived += OnCameraFrameReceived;
        _camera.PropertyChanged += OnCameraMediatorPropertyChanged;
        _filterWheel.PropertyChanged += OnFilterWheelPropertyChanged;
        _capture.CaptureStarted += OnCaptureStarted;
        _capture.CaptureStopped += OnCaptureStopped;
        _capture.FrameCaptured += OnFrameCaptured;

        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _statsTimer.Tick += (_, _) => RefreshCaptureStats();

        OutputDirectory = _capture.OutputDirectory;
        FilePrefix = _capture.FilePrefix;
        FrameLimit = _capture.FrameLimit;
        TimeLimitSeconds = _capture.TimeLimitSeconds;
        SelectedFormat = _capture.Format;

        RefreshCameraDisplayName();
        RebuildFilterQuickList();
    }

    [ObservableProperty]
    private bool isCapturing;

    [ObservableProperty]
    private int framesCaptured;

    [ObservableProperty]
    private double currentFps;

    [ObservableProperty]
    private int framesDropped;

    [ObservableProperty]
    private long estimatedSessionBytes;

    [ObservableProperty]
    private int frameLimit;

    [ObservableProperty]
    private double timeLimitSeconds;

    [ObservableProperty]
    private string outputDirectory = string.Empty;

    [ObservableProperty]
    private string filePrefix = "capture";

    [ObservableProperty]
    private CaptureFormat selectedFormat;

    [ObservableProperty]
    private WriteableBitmap? liveImage;

    [ObservableProperty]
    private PointCollection histogramLinePoints = new();

    [ObservableProperty]
    private int histogramMin;

    [ObservableProperty]
    private int histogramMax;

    [ObservableProperty]
    private double histogramMean;

    [ObservableProperty]
    private double exposureMicroseconds = 10_000;

    /// <summary>Normalized 0–1 slider position for logarithmic exposure mapping.</summary>
    [ObservableProperty]
    private double exposureSliderValue;

    [ObservableProperty]
    private double gain;

    [ObservableProperty]
    private double exposureMin;

    [ObservableProperty]
    private double exposureMax = 30_000_000;

    [ObservableProperty]
    private double gainMin;

    [ObservableProperty]
    private double gainMax = 100;

    [ObservableProperty]
    private int roiOffsetX;

    [ObservableProperty]
    private int roiOffsetY;

    [ObservableProperty]
    private int roiWidth = 640;

    [ObservableProperty]
    private int roiHeight = 480;

    [ObservableProperty]
    private string connectedCameraDisplayName = "—";

    [ObservableProperty]
    private string cameraSensorLine = "—";

    [ObservableProperty]
    private string currentFilterQuickName = "—";

    [ObservableProperty]
    private ObservableCollection<FilterSlotRow> filterQuickSlots = new();

    [ObservableProperty]
    private bool showCrosshair;

    [ObservableProperty]
    private double liveImageZoom = 1.0;

    partial void OnFrameLimitChanged(int value)
    {
        _capture.FrameLimit = value;
    }

    partial void OnTimeLimitSecondsChanged(double value)
    {
        _capture.TimeLimitSeconds = value;
    }

    partial void OnOutputDirectoryChanged(string value)
    {
        _capture.OutputDirectory = string.IsNullOrWhiteSpace(value) ? Path.GetTempPath() : value;
    }

    partial void OnFilePrefixChanged(string value)
    {
        _capture.FilePrefix = string.IsNullOrWhiteSpace(value) ? "capture" : value;
    }

    partial void OnSelectedFormatChanged(CaptureFormat value)
    {
        _capture.Format = value;
    }

    partial void OnExposureMicrosecondsChanged(double value)
    {
        SyncExposureSliderFromModel();
        ApplyCameraExposureGain();
    }

    partial void OnExposureSliderValueChanged(double value)
    {
        if (_exposureSliderDrive)
            return;

        double min = Math.Max(ExposureMin, 1e-3);
        double max = Math.Max(ExposureMax, min * 1.001);
        if (max <= min * 1.0001)
        {
            ExposureMicroseconds = min;
            return;
        }

        double logMin = Math.Log(min);
        double logMax = Math.Log(max);
        double exp = Math.Exp(logMin + Math.Clamp(value, 0, 1) * (logMax - logMin));
        if (Math.Abs(exp - ExposureMicroseconds) > 0.5)
            ExposureMicroseconds = exp;
    }

    partial void OnExposureMinChanged(double value) => SyncExposureSliderFromModel();

    partial void OnExposureMaxChanged(double value) => SyncExposureSliderFromModel();

    partial void OnGainChanged(double value)
    {
        ApplyCameraExposureGain();
    }

    private void SyncExposureSliderFromModel()
    {
        double min = Math.Max(ExposureMin, 1e-3);
        double max = Math.Max(ExposureMax, min * 1.001);
        _exposureSliderDrive = true;
        try
        {
            if (max <= min * 1.0001)
            {
                ExposureSliderValue = 0;
                return;
            }

            double v = Math.Clamp(ExposureMicroseconds, min, max);
            double logMin = Math.Log(min);
            double logMax = Math.Log(max);
            double t = (Math.Log(v) - logMin) / (logMax - logMin);
            ExposureSliderValue = Math.Clamp(t, 0, 1);
        }
        finally
        {
            _exposureSliderDrive = false;
        }
    }

    private void ApplyCameraExposureGain()
    {
        ICameraProvider? cam = _camera.GetConnectedProvider();
        if (cam is null || !cam.IsConnected)
            return;
        try
        {
            cam.SetExposure(ExposureMicroseconds);
            cam.SetGain(Gain);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "CaptureViewModel: failed to set exposure/gain.");
        }
    }

    private void RefreshCameraLimits()
    {
        ICameraProvider? cam = _camera.GetConnectedProvider();
        if (cam is null || !cam.IsConnected)
            return;
        ExposureMin = cam.ExposureMin;
        ExposureMax = cam.ExposureMax;
        GainMin = cam.GainMin;
        GainMax = cam.GainMax;
        SyncExposureSliderFromModel();
    }

    private void RefreshCameraDisplayName()
    {
        ConnectedCameraDisplayName = _camera.IsConnected
            ? _camera.ConnectedDeviceName ?? "Camera"
            : "—";
        ICameraProvider? cam = _camera.GetConnectedProvider();
        if (cam is null || !cam.IsConnected)
        {
            CameraSensorLine = "—";
            return;
        }

        CameraSensorLine = $"{cam.SensorWidth}×{cam.SensorHeight} px · {cam.PixelSizeUm:F2} µm · {(cam.IsColor ? "Color" : "Mono")}";
    }

    private void OnCameraMediatorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CameraMediator.IsConnected)
            or nameof(CameraMediator.ConnectedDeviceName)
            or null or "")
        {
            _dispatcher.BeginInvoke(() =>
            {
                RefreshCameraDisplayName();
                RebuildFilterQuickList();
            });
        }
    }

    private void OnFilterWheelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _dispatcher.BeginInvoke(RebuildFilterQuickList);
    }

    private void RebuildFilterQuickList()
    {
        if (!_filterWheel.IsConnected)
        {
            CurrentFilterQuickName = "—";
            FilterQuickSlots = new ObservableCollection<FilterSlotRow>();
            return;
        }

        int pos = _filterWheel.CurrentPosition;
        IReadOnlyList<string> names = _filterWheel.FilterNames;
        CurrentFilterQuickName = pos >= 0 && pos < names.Count
            ? (string.IsNullOrWhiteSpace(names[pos]) ? $"Slot {pos}" : names[pos])
            : "—";

        var rows = new List<FilterSlotRow>();
        for (int i = 0; i < names.Count; i++)
        {
            rows.Add(new FilterSlotRow
            {
                Index = i,
                Name = string.IsNullOrWhiteSpace(names[i]) ? $"{i}" : names[i],
                IsCurrent = i == pos,
            });
        }

        FilterQuickSlots = new ObservableCollection<FilterSlotRow>(rows);
    }

    private void OnCameraFrameReceived(object? sender, FrameData frame)
    {
        _lastFramePayloadBytes = frame.Data.Length;
        _dispatcher.BeginInvoke(() =>
        {
            LiveImage = MatBitmapHelper.FrameToWriteableBitmap(frame);
            UpdateHistogram(frame);
            RefreshCameraLimits();
            RefreshCameraDisplayName();
        });
    }

    private void UpdateHistogram(FrameData frame)
    {
        try
        {
            using Mat mat = Debayer.ToMat(frame);
            using Mat gray = mat.Channels() == 1
                ? mat.Clone()
                : new Mat();
            if (mat.Channels() != 1)
                Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY);

            Cv2.MinMaxLoc(gray, out double minVal, out double maxVal);
            Scalar mean = Cv2.Mean(gray);
            HistogramMin = (int)Math.Clamp(Math.Round(minVal), 0, 255);
            HistogramMax = (int)Math.Clamp(Math.Round(maxVal), 0, 255);
            HistogramMean = mean.Val0;

            const int bins = 256;
            using Mat histMat = new Mat();
            Cv2.CalcHist(new[] { gray }, new[] { 0 }, null, histMat, 1, new[] { bins }, new[] { new Rangef(0, 256) });
            double maxBin = 1;
            for (int i = 0; i < bins; i++)
            {
                float v = histMat.At<float>(i);
                if (v > maxBin)
                    maxBin = v;
            }

            var pts = new PointCollection();
            double w = 1000;
            double h = 200;
            for (int i = 0; i < bins; i++)
            {
                float c = histMat.At<float>(i);
                double x = bins <= 1 ? 0 : i / (double)(bins - 1) * w;
                double y = h - (c / maxBin) * h;
                pts.Add(new System.Windows.Point(x, y));
            }

            HistogramLinePoints = pts;
        }
        catch
        {
            // ignore histogram errors for unsupported transient frames
        }
    }

    private void OnCaptureStarted(object? sender, EventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            IsCapturing = true;
            _statsTimer.Start();
            RefreshCaptureStats();
        });
    }

    private void OnCaptureStopped(object? sender, EventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            IsCapturing = false;
            _statsTimer.Stop();
            RefreshCaptureStats();
        });
    }

    private void OnFrameCaptured(object? sender, FrameData e)
    {
        _dispatcher.BeginInvoke(RefreshCaptureStats);
    }

    private void RefreshCaptureStats()
    {
        FramesCaptured = _capture.FramesCaptured;
        CurrentFps = _capture.CurrentFps;
        FramesDropped = _capture.FramesDropped;
        IsCapturing = _capture.IsCapturing;
        EstimatedSessionBytes = IsCapturing && FramesCaptured > 0 && _lastFramePayloadBytes > 0
            ? (long)FramesCaptured * _lastFramePayloadBytes
            : 0;
    }

    [RelayCommand]
    private void ZoomIn()
    {
        LiveImageZoom = Math.Min(4, LiveImageZoom + 0.25);
    }

    [RelayCommand]
    private void ZoomOut()
    {
        LiveImageZoom = Math.Max(1, LiveImageZoom - 0.25);
    }

    [RelayCommand]
    private void ResetZoom()
    {
        LiveImageZoom = 1;
    }

    [RelayCommand]
    private void StartCapture()
    {
        if (_capture.IsCapturing)
            return;
        if (!_camera.IsConnected)
            return;
        RefreshCameraLimits();
        ApplyCameraExposureGain();
        _capture.StartCapture(_camera);
    }

    [RelayCommand]
    private void StopCapture()
    {
        _capture.StopCapture();
    }

    [RelayCommand]
    private void BrowseOutput()
    {
        var dlg = new OpenFolderDialog
        {
            InitialDirectory = Directory.Exists(OutputDirectory) ? OutputDirectory : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog() == true)
            OutputDirectory = dlg.FolderName;
    }

    [RelayCommand]
    private void ApplyRoi()
    {
        ICameraProvider? cam = _camera.GetConnectedProvider();
        if (cam is null || !cam.IsConnected)
            return;
        try
        {
            cam.SetROI(RoiOffsetX, RoiOffsetY, RoiWidth, RoiHeight);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "CaptureViewModel: SetROI failed.");
        }
    }

    [RelayCommand]
    private void ResetRoi()
    {
        ICameraProvider? cam = _camera.GetConnectedProvider();
        if (cam is null || !cam.IsConnected)
            return;
        try
        {
            cam.ResetROI();
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "CaptureViewModel: ResetROI failed.");
        }
    }

    [RelayCommand]
    private async Task SelectFilterQuickAsync(int index)
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

        RebuildFilterQuickList();
    }

    public void Dispose()
    {
        _statsTimer.Stop();
        _camera.FrameReceived -= OnCameraFrameReceived;
        _camera.PropertyChanged -= OnCameraMediatorPropertyChanged;
        _filterWheel.PropertyChanged -= OnFilterWheelPropertyChanged;
        _capture.CaptureStarted -= OnCaptureStarted;
        _capture.CaptureStopped -= OnCaptureStopped;
        _capture.FrameCaptured -= OnFrameCaptured;
        GC.SuppressFinalize(this);
    }
}
