using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NINA.PL.AutoFocus;
using NINA.PL.Core;

namespace NINA.PL.WPF.ViewModels;

public sealed partial class FocusViewModel : ObservableObject, IDisposable
{
    public IReadOnlyList<FocusMetricType> FocusMetricTypes { get; } = Enum.GetValues<FocusMetricType>().ToArray();

    private readonly AutoFocusEngine _engine;
    private readonly CameraMediator _camera;
    private readonly FocuserMediator _focuser;
    private CancellationTokenSource? _runCts;

    public FocusViewModel(AutoFocusEngine engine, CameraMediator camera, FocuserMediator focuser)
    {
        _engine = engine;
        _camera = camera;
        _focuser = focuser;

        _engine.PropertyChanged += OnEnginePropertyChanged;
        _engine.FocusCompleted += OnFocusCompleted;
        _engine.FocusFailed += OnFocusFailed;
        _engine.PointMeasured += OnPointMeasured;
        _focuser.PropertyChanged += OnFocuserMediatorPropertyChanged;
        _engine.FocusCurve.CollectionChanged += OnFocusCurveCollectionChanged;

        MetricType = _engine.MetricType;
        StepSize = _engine.StepSize;
        NumberOfSteps = _engine.NumberOfSteps;
        SettleTimeMs = _engine.SettleTimeMs;
        IsRunning = _engine.IsRunning;
        SyncFocuserTelemetry();
        RebuildFocusPolyline();
    }

    [ObservableProperty]
    private bool isRunning;

    [ObservableProperty]
    private FocusMetricType metricType;

    [ObservableProperty]
    private int stepSize;

    [ObservableProperty]
    private int numberOfSteps;

    [ObservableProperty]
    private int settleTimeMs;

    public ObservableCollection<FocusPoint> FocusCurve => _engine.FocusCurve;

    [ObservableProperty]
    private int bestPosition;

    [ObservableProperty]
    private double lastBestMetric;

    [ObservableProperty]
    private double focusCurveMaxMetric = 1;

    [ObservableProperty]
    private string focusMessage = string.Empty;

    [ObservableProperty]
    private PointCollection focusCurvePoints = new();

    [ObservableProperty]
    private int focuserPosition;

    [ObservableProperty]
    private int focuserMaxPosition;

    [ObservableProperty]
    private int focuserTargetPosition;

    partial void OnMetricTypeChanged(FocusMetricType value)
    {
        _engine.MetricType = value;
    }

    partial void OnStepSizeChanged(int value)
    {
        _engine.StepSize = value;
    }

    partial void OnNumberOfStepsChanged(int value)
    {
        _engine.NumberOfSteps = value;
    }

    partial void OnSettleTimeMsChanged(int value)
    {
        _engine.SettleTimeMs = value;
    }

    private void OnFocusCurveCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateMaxMetric();
        RebuildFocusPolyline();
    }

    private void OnEnginePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AutoFocusEngine.IsRunning))
            IsRunning = _engine.IsRunning;
    }

    private void OnFocusCompleted(object? sender, int best)
    {
        BestPosition = best;
        double metric = 0;
        foreach (FocusPoint p in FocusCurve)
        {
            if (p.Position == best)
            {
                metric = p.MetricValue;
                break;
            }
        }

        if (metric <= 0 && FocusCurve.Count > 0)
        {
            FocusPoint? nearest = FocusCurve.OrderBy(p => Math.Abs(p.Position - best)).FirstOrDefault();
            metric = nearest?.MetricValue ?? 0;
        }

        LastBestMetric = metric;
        FocusMessage = $"Autofocus complete. Best position: {best}.";
        UpdateMaxMetric();
        RebuildFocusPolyline();
    }

    private void OnFocusFailed(object? sender, string message)
    {
        FocusMessage = message;
    }

    private void OnPointMeasured(object? sender, FocusPoint e)
    {
        UpdateMaxMetric();
        RebuildFocusPolyline();
    }

    private void OnFocuserMediatorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Application.Current.Dispatcher.BeginInvoke(SyncFocuserTelemetry);
    }

    private void SyncFocuserTelemetry()
    {
        if (!_focuser.IsConnected)
        {
            FocuserPosition = 0;
            FocuserMaxPosition = 0;
            return;
        }

        FocuserPosition = _focuser.Position;
        FocuserMaxPosition = _focuser.MaxPosition;
    }

    private void UpdateMaxMetric()
    {
        double max = 1;
        foreach (FocusPoint p in FocusCurve)
        {
            if (p.MetricValue > max)
                max = p.MetricValue;
        }

        FocusCurveMaxMetric = max <= 0 ? 1 : max;
    }

    private void RebuildFocusPolyline()
    {
        var pts = new PointCollection();
        if (FocusCurve.Count == 0)
        {
            FocusCurvePoints = pts;
            return;
        }

        var ordered = FocusCurve.OrderBy(p => p.Position).ToList();
        double minX = ordered[0].Position;
        double maxX = ordered[^1].Position;
        double spanX = Math.Max(maxX - minX, 1);
        double maxY = FocusCurveMaxMetric <= 0 ? 1 : FocusCurveMaxMetric;
        const double w = 800;
        const double h = 220;

        foreach (FocusPoint p in ordered)
        {
            double x = (p.Position - minX) / spanX * w;
            double y = h - (p.MetricValue / maxY) * h * 0.92 - 8;
            pts.Add(new System.Windows.Point(x, Math.Clamp(y, 4, h - 4)));
        }

        FocusCurvePoints = pts;
    }

    [RelayCommand]
    private async Task RunAutoFocusAsync()
    {
        IFocuserProvider? foc = _focuser.GetConnectedProvider();
        ICameraProvider? cam = _camera.GetConnectedProvider();
        if (foc is null || cam is null)
        {
            FocusMessage = "Connect camera and focuser first.";
            return;
        }

        _runCts = new CancellationTokenSource();
        FocusMessage = "Running autofocus…";
        try
        {
            await _engine.RunAutoFocusAsync(foc, cam, _runCts.Token).ConfigureAwait(true);
            UpdateMaxMetric();
            RebuildFocusPolyline();
        }
        catch (OperationCanceledException)
        {
            FocusMessage = "Autofocus cancelled.";
        }
        catch (Exception ex)
        {
            FocusMessage = ex.Message;
        }
        finally
        {
            _runCts.Dispose();
            _runCts = null;
        }
    }

    [RelayCommand]
    private void Abort()
    {
        _runCts?.Cancel();
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
        }

        SyncFocuserTelemetry();
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

        SyncFocuserTelemetry();
    }

    public void Dispose()
    {
        _engine.PropertyChanged -= OnEnginePropertyChanged;
        _engine.FocusCompleted -= OnFocusCompleted;
        _engine.FocusFailed -= OnFocusFailed;
        _engine.PointMeasured -= OnPointMeasured;
        _focuser.PropertyChanged -= OnFocuserMediatorPropertyChanged;
        _engine.FocusCurve.CollectionChanged -= OnFocusCurveCollectionChanged;
        _runCts?.Cancel();
        _runCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
