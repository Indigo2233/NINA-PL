using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NINA.PL.Core;
using NINA.PL.Image;
using OpenCvSharp;

namespace NINA.PL.AutoFocus;

/// <summary>
/// Orchestrates planetary autofocus: sweeps focuser positions, scores frames, fits a curve, and moves to the optimum.
/// </summary>
public sealed class AutoFocusEngine : INotifyPropertyChanged
{
    private const int FrameCaptureMaxAttempts = 3;
    private const int MinimumFrameTimeoutMs = 3000;

    private bool _isRunning;
    private FocusMetricType _metricType = FocusMetricType.ContrastSobel;
    private int _stepSize = 100;
    private int _numberOfSteps = 11;
    private int _settleTimeMs = 500;

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<FocusPoint>? PointMeasured;

    public event EventHandler<int>? FocusCompleted;

    public event EventHandler<string>? FocusFailed;

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (_isRunning == value)
            {
                return;
            }

            _isRunning = value;
            OnPropertyChanged();
        }
    }

    public FocusMetricType MetricType
    {
        get => _metricType;
        set
        {
            if (_metricType == value)
            {
                return;
            }

            _metricType = value;
            OnPropertyChanged();
        }
    }

    public int StepSize
    {
        get => _stepSize;
        set
        {
            if (_stepSize == value)
            {
                return;
            }

            _stepSize = value;
            OnPropertyChanged();
        }
    }

    public int NumberOfSteps
    {
        get => _numberOfSteps;
        set
        {
            if (_numberOfSteps == value)
            {
                return;
            }

            _numberOfSteps = value;
            OnPropertyChanged();
        }
    }

    public int SettleTimeMs
    {
        get => _settleTimeMs;
        set
        {
            if (_settleTimeMs == value)
            {
                return;
            }

            _settleTimeMs = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Measured samples from the latest (or current) autofocus run.</summary>
    public ObservableCollection<FocusPoint> FocusCurve { get; } = new();

    public async Task RunAutoFocusAsync(IFocuserProvider focuser, ICameraProvider camera, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(focuser);
        ArgumentNullException.ThrowIfNull(camera);

        if (IsRunning)
        {
            RaiseFocusFailed("Autofocus is already running.");
            return;
        }

        if (!focuser.IsConnected)
        {
            RaiseFocusFailed("Focuser is not connected.");
            return;
        }

        if (!camera.IsConnected)
        {
            RaiseFocusFailed("Camera is not connected.");
            return;
        }

        if (StepSize <= 0)
        {
            RaiseFocusFailed("StepSize must be positive.");
            return;
        }

        if (NumberOfSteps < 1)
        {
            RaiseFocusFailed("NumberOfSteps must be at least 1.");
            return;
        }

        int initialPosition = focuser.Position;
        int maxPos = focuser.MaxPosition;
        bool captureStarted = false;

        IsRunning = true;
        FocusCurve.Clear();

        try
        {
            await camera.StartCaptureAsync().ConfigureAwait(false);
            captureStarted = true;

            int halfSpan = checked(StepSize * NumberOfSteps / 2);
            int startTarget = Clamp(initialPosition - halfSpan, 0, maxPos);

            try
            {
                await focuser.MoveAsync(startTarget).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Autofocus: failed to move focuser to start position {Start}.", startTarget);
                RaiseFocusFailed($"Failed to move focuser to start position: {ex.Message}");
                return;
            }

            for (int i = 0; i < NumberOfSteps; i++)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    await focuser.MoveRelativeAsync(StepSize).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Autofocus: MoveRelativeAsync failed (offset {Offset}).", StepSize);
                    RaiseFocusFailed($"Focuser relative move failed: {ex.Message}");
                    return;
                }

                try
                {
                    await Task.Delay(SettleTimeMs, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }

                int posAfterSettle = focuser.Position;
                FrameData? frame = await TryCaptureFrameAsync(camera, ct).ConfigureAwait(false);
                if (frame is null)
                {
                    RaiseFocusFailed($"Timed out waiting for a frame at focuser position {posAfterSettle}.");
                    return;
                }

                double metric;
                try
                {
                    using Mat mat = Debayer.ToMat(frame);
                    metric = FocusMetricCalculator.Calculate(mat, MetricType);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Autofocus: metric computation failed at position {Position}.", posAfterSettle);
                    RaiseFocusFailed($"Failed to compute focus metric: {ex.Message}");
                    return;
                }

                var point = new FocusPoint
                {
                    Position = posAfterSettle,
                    MetricValue = metric,
                    MetricType = MetricType,
                };

                FocusCurve.Add(point);
                PointMeasured?.Invoke(this, point);
            }

            if (FocusCurve.Count == 0)
            {
                RaiseFocusFailed("No focus samples were recorded.");
                return;
            }

            var snapshot = new System.Collections.Generic.List<FocusPoint>(FocusCurve);
            int bestPosition;
            try
            {
                bestPosition = FocusCurveFitter.FindBestPosition(snapshot);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Autofocus: curve fit failed.");
                RaiseFocusFailed($"Focus curve fit failed: {ex.Message}");
                return;
            }

            bestPosition = Clamp(bestPosition, 0, maxPos);

            try
            {
                await focuser.MoveAsync(bestPosition).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Autofocus: failed to move to best position {Best}.", bestPosition);
                RaiseFocusFailed($"Failed to move to best focus position: {ex.Message}");
                return;
            }

            FocusCompleted?.Invoke(this, bestPosition);
        }
        catch (OperationCanceledException)
        {
            RaiseFocusFailed("Autofocus was cancelled.");
            try
            {
                await focuser.MoveAsync(initialPosition).ConfigureAwait(false);
            }
            catch (Exception restoreEx)
            {
                Logger.Warn(restoreEx, "Autofocus: could not restore focuser to initial position {Initial}.", initialPosition);
            }

            throw;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Autofocus: unexpected error.");
            RaiseFocusFailed($"Autofocus failed: {ex.Message}");
            try
            {
                await focuser.MoveAsync(initialPosition).ConfigureAwait(false);
            }
            catch (Exception restoreEx)
            {
                Logger.Warn(restoreEx, "Autofocus: could not restore focuser to initial position {Initial}.", initialPosition);
            }
        }
        finally
        {
            if (captureStarted)
            {
                try
                {
                    await camera.StopCaptureAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "Autofocus: StopCaptureAsync failed for camera {Driver}.", camera.DriverType);
                }
            }

            IsRunning = false;
        }
    }

    private async Task<FrameData?> TryCaptureFrameAsync(ICameraProvider camera, CancellationToken ct)
    {
        int timeoutMs = Math.Max(MinimumFrameTimeoutMs, SettleTimeMs * 4);

        for (int attempt = 0; attempt < FrameCaptureMaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var tcs = new TaskCompletionSource<FrameData>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnFrame(object? sender, FrameData e)
            {
                tcs.TrySetResult(e);
            }

            camera.FrameReceived += OnFrame;
            try
            {
                Task<FrameData> frameTask = tcs.Task;
                Task timeoutTask = Task.Delay(timeoutMs, ct);
                Task winner = await Task.WhenAny(frameTask, timeoutTask).ConfigureAwait(false);
                if (ReferenceEquals(winner, frameTask) && frameTask.IsCompletedSuccessfully)
                {
                    return await frameTask.ConfigureAwait(false);
                }
            }
            finally
            {
                camera.FrameReceived -= OnFrame;
            }

            Logger.Warn(
                "Autofocus: frame capture attempt {Attempt}/{Max} timed out after {TimeoutMs} ms.",
                attempt + 1,
                FrameCaptureMaxAttempts,
                timeoutMs);
        }

        return null;
    }

    private void RaiseFocusFailed(string message)
    {
        FocusFailed?.Invoke(this, message);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }
}
