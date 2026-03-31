using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NINA.PL.Core;
using NINA.PL.LiveStack;
using NINA.PL.WPF.Helpers;
using OpenCvSharp;

namespace NINA.PL.WPF.ViewModels;

public sealed partial class LiveStackViewModel : ObservableObject, IDisposable
{
    public IReadOnlyList<StackingMode> StackingModes { get; } = Enum.GetValues<StackingMode>().ToArray();

    private readonly LiveStackEngine _engine;
    private readonly CameraMediator _camera;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _statsTimer;

    public LiveStackViewModel(LiveStackEngine engine, CameraMediator camera)
    {
        _engine = engine;
        _camera = camera;
        _dispatcher = Application.Current.Dispatcher;

        _engine.StackUpdated += OnStackUpdated;
        _engine.StackingStarted += OnStackingStarted;
        _engine.StackingStopped += OnStackingStopped;

        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _statsTimer.Tick += (_, _) => RefreshStats();

        SelectedMode = _engine.Mode;
        QualityThreshold = _engine.QualityThreshold;
        double[] w = _engine.WaveletWeights;
        if (w.Length > 0) WaveletLayer1 = w[0];
        if (w.Length > 1) WaveletLayer2 = w[1];
        if (w.Length > 2) WaveletLayer3 = w[2];
        if (w.Length > 3) WaveletLayer4 = w[3];
        if (w.Length > 4) WaveletLayer5 = w[4];
        if (w.Length > 5) WaveletLayer6 = w[5];
    }

    [ObservableProperty]
    private bool isRunning;

    [ObservableProperty]
    private int totalFrames;

    [ObservableProperty]
    private int acceptedFrames;

    [ObservableProperty]
    private int rejectedFrames;

    [ObservableProperty]
    private double acceptanceRatePercent;

    [ObservableProperty]
    private double qualityThreshold;

    [ObservableProperty]
    private WriteableBitmap? stackedImage;

    [ObservableProperty]
    private StackingMode selectedMode;

    [ObservableProperty]
    private double waveletLayer1 = 1.5;

    [ObservableProperty]
    private double waveletLayer2 = 1.5;

    [ObservableProperty]
    private double waveletLayer3 = 1.0;

    [ObservableProperty]
    private double waveletLayer4 = 1.0;

    [ObservableProperty]
    private double waveletLayer5 = 0.5;

    [ObservableProperty]
    private double waveletLayer6 = 0.5;

    partial void OnQualityThresholdChanged(double value)
    {
        _engine.QualityThreshold = value;
    }

    partial void OnSelectedModeChanged(StackingMode value)
    {
        _engine.Mode = value;
    }

    partial void OnWaveletLayer1Changed(double value) => PushWaveletWeights();

    partial void OnWaveletLayer2Changed(double value) => PushWaveletWeights();

    partial void OnWaveletLayer3Changed(double value) => PushWaveletWeights();

    partial void OnWaveletLayer4Changed(double value) => PushWaveletWeights();

    partial void OnWaveletLayer5Changed(double value) => PushWaveletWeights();

    partial void OnWaveletLayer6Changed(double value) => PushWaveletWeights();

    private void PushWaveletWeights()
    {
        _engine.WaveletWeights = new[]
        {
            WaveletLayer1, WaveletLayer2, WaveletLayer3,
            WaveletLayer4, WaveletLayer5, WaveletLayer6,
        };
    }

    private void OnStackUpdated(object? sender, Mat mat)
    {
        try
        {
            using Mat clone = mat.Clone();
            WriteableBitmap? bmp = MatBitmapHelper.MatToWriteableBitmap(clone);
            _dispatcher.BeginInvoke(() => StackedImage = bmp);
        }
        catch
        {
            // ignore preview conversion errors
        }
    }

    private void OnStackingStarted(object? sender, EventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            IsRunning = true;
            _statsTimer.Start();
        });
    }

    private void OnStackingStopped(object? sender, EventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            IsRunning = false;
            _statsTimer.Stop();
            RefreshStats();
        });
    }

    private void RefreshStats()
    {
        TotalFrames = _engine.TotalFrames;
        AcceptedFrames = _engine.AcceptedFrames;
        RejectedFrames = _engine.RejectedFrames;
        IsRunning = _engine.IsRunning;
        AcceptanceRatePercent = TotalFrames <= 0 ? 0 : 100.0 * AcceptedFrames / TotalFrames;
    }

    [RelayCommand]
    private void Start()
    {
        if (!_camera.IsConnected)
            return;
        _engine.Start(_camera);
        RefreshStats();
    }

    [RelayCommand]
    private void Stop()
    {
        _engine.Stop();
        RefreshStats();
    }

    [RelayCommand]
    private void Reset()
    {
        _engine.Reset();
        RefreshStats();
        StackedImage = null;
    }

    public void Dispose()
    {
        _statsTimer.Stop();
        _engine.StackUpdated -= OnStackUpdated;
        _engine.StackingStarted -= OnStackingStarted;
        _engine.StackingStopped -= OnStackingStopped;
        GC.SuppressFinalize(this);
    }
}
