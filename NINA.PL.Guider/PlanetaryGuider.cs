using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NINA.PL.Core;
using NINA.PL.Guider.TrackingAlgorithms;
using NINA.PL.Image;
using OpenCvSharp;

namespace NINA.PL.Guider;

public enum TrackingMode
{
    DiskCentroid,
    Limb,
    SurfaceFeature,
}

/// <summary>
/// Event-driven planetary guiding loop: frame acquisition drives tracking, calibration maps pixels to sky, PID emits pulse guides.
/// </summary>
public sealed class PlanetaryGuider
{
    private const int MaxHistory = 100;
    private const double MinTemplateConfidence = 0.25;

    private readonly object _sync = new();
    private readonly List<GuideStep> _history = new(MaxHistory);
    private readonly DiskCentroidTracker _disk = new();
    private readonly LimbTracker _limb = new();
    private readonly SurfaceFeatureTracker _surface = new();
    private readonly PIDController _pidRa = new(kP: 45, kI: 1.5, kD: 8, maxOutput: 4000);
    private readonly PIDController _pidDec = new(kP: 45, kI: 1.5, kD: 8, maxOutput: 4000);

    private Channel<FrameData>? _frameChannel;
    private CancellationTokenSource? _guideCts;
    private CameraMediator? _boundCamera;
    private DateTime _lastStepUtc;
    private double _refX;
    private double _refY;
    private bool _referenceReady;

    public GuiderCalibration Calibration { get; } = new();

    public bool IsGuiding { get; private set; }

    public bool IsCalibrated => Calibration.IsCalibrated;

    /// <summary>Last 100 guiding steps (newest last). Snapshot copy.</summary>
    public List<GuideStep> History
    {
        get
        {
            lock (_sync)
                return new List<GuideStep>(_history);
        }
    }

    public double RmsTotal { get; private set; }

    /// <summary>Minimum template match score before applying Dec/RA corrections in <see cref="TrackingMode.SurfaceFeature"/>.</summary>
    public double MinMatchConfidence { get; set; } = MinTemplateConfidence;

    /// <summary>Surface ROI when <see cref="SurfaceRoiX"/> or <see cref="SurfaceRoiY"/> are negative (auto-centered).</summary>
    public int SurfaceRoiWidth { get; set; } = 128;

    public int SurfaceRoiHeight { get; set; } = 128;

    /// <summary>Use ≥0 for fixed origin; &lt;0 centers ROI in the reference frame.</summary>
    public int SurfaceRoiX { get; set; } = -1;

    public int SurfaceRoiY { get; set; } = -1;

    public event EventHandler<GuideStep>? GuidingStep;
    public event EventHandler? GuidingStarted;
    public event EventHandler? GuidingStopped;

    public async Task StartGuidingAsync(
        CameraMediator camera,
        MountMediator mount,
        TrackingMode mode,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(mount);

        if (IsGuiding)
            throw new InvalidOperationException("Guiding is already running.");

        if (!Calibration.IsCalibrated)
            throw new InvalidOperationException("Calibrate before starting guiding.");

        ICameraProvider? cam = camera.GetConnectedProvider();
        IMountProvider? mnt = mount.GetConnectedProvider();
        if (cam is null || !cam.IsConnected)
            throw new InvalidOperationException("No connected camera.");
        if (mnt is null || !mnt.IsConnected || !mnt.CanPulseGuide)
            throw new InvalidOperationException("No connected mount or pulse guiding unavailable.");

        _guideCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        CancellationToken guideToken = _guideCts.Token;

        _frameChannel = Channel.CreateBounded<FrameData>(new BoundedChannelOptions(2)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        _boundCamera = camera;
        camera.FrameReceived += OnCameraFrame;

        IsGuiding = true;
        _referenceReady = false;
        _lastStepUtc = default;
        _pidRa.Reset();
        _pidDec.Reset();

        GuidingStarted?.Invoke(this, EventArgs.Empty);

        try
        {
            await RunGuideLoopAsync(camera, mount, mode, guideToken).ConfigureAwait(false);
        }
        finally
        {
            if (_boundCamera is not null)
            {
                _boundCamera.FrameReceived -= OnCameraFrame;
                _boundCamera = null;
            }

            _frameChannel?.Writer.TryComplete();
            _frameChannel = null;
            _guideCts?.Dispose();
            _guideCts = null;
            IsGuiding = false;
            GuidingStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    public void StopGuiding() => _guideCts?.Cancel();

    private void OnCameraFrame(object? sender, FrameData e)
    {
        Channel<FrameData>? ch = _frameChannel;
        if (ch is null)
            return;

        _ = ch.Writer.TryWrite(CloneFrame(e));
    }

    private async Task RunGuideLoopAsync(
        CameraMediator camera,
        MountMediator mount,
        TrackingMode mode,
        CancellationToken ct)
    {
        Channel<FrameData> ch = _frameChannel ?? throw new InvalidOperationException("Frame channel not initialized.");

        IMountProvider mnt = mount.GetConnectedProvider()!;

        await foreach (FrameData frame in ch.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();

            using Mat mat = Debayer.ToMat(frame);

            if (!_referenceReady)
            {
                InitializeReference(mat, mode);
                _lastStepUtc = frame.Timestamp;
                continue;
            }

            double dx;
            double dy;
            if (mode == TrackingMode.SurfaceFeature)
            {
                var (tdx, tdy, conf) = _surface.Track(mat);
                if (conf < MinMatchConfidence)
                    continue;

                dx = tdx;
                dy = tdy;
            }
            else if (mode == TrackingMode.Limb)
            {
                var (cx, cy) = _limb.DetectLimb(mat);
                dx = cx - _refX;
                dy = cy - _refY;
            }
            else
            {
                (dx, dy) = _disk.ComputeOffset(mat, _refX, _refY);
            }

            (double eRa, double eDec) = Calibration.PixelToArcSec(dx, dy);

            double dt = _lastStepUtc == default
                ? Math.Max(frame.ExposureUs / 1_000_000.0, 0.02)
                : Math.Max((frame.Timestamp - _lastStepUtc).TotalSeconds, 1e-3);
            _lastStepUtc = frame.Timestamp;

            double raMs = _pidRa.Compute(eRa, dt);
            double decMs = _pidDec.Compute(eDec, dt);

            int raPulse = (int)Math.Round(Math.Clamp(Math.Abs(raMs), 0, 4000));
            int decPulse = (int)Math.Round(Math.Clamp(Math.Abs(decMs), 0, 4000));

            if (raPulse > 0)
            {
                GuideDirection d = raMs > 0 ? GuideDirection.East : GuideDirection.West;
                await mount.PulseGuideAsync(d, raPulse, ct).ConfigureAwait(false);
            }

            if (decPulse > 0)
            {
                GuideDirection d = decMs > 0 ? GuideDirection.North : GuideDirection.South;
                await mount.PulseGuideAsync(d, decPulse, ct).ConfigureAwait(false);
            }

            double rmsSample = Math.Sqrt(eRa * eRa + eDec * eDec);
            var step = new GuideStep
            {
                Timestamp = frame.Timestamp,
                RaErrorArcSec = eRa,
                DecErrorArcSec = eDec,
                RaCorrectionMs = raPulse * Math.Sign(raMs),
                DecCorrectionMs = decPulse * Math.Sign(decMs),
                RmsArcSec = rmsSample,
            };

            AppendHistory(step);
            GuidingStep?.Invoke(this, step);
        }
    }

    private void InitializeReference(Mat mat, TrackingMode mode)
    {
        switch (mode)
        {
            case TrackingMode.DiskCentroid:
                var (dcx, dcy, _) = _disk.DetectDisk(mat);
                _refX = dcx;
                _refY = dcy;
                break;
            case TrackingMode.Limb:
                var (lcx, lcy) = _limb.DetectLimb(mat);
                _refX = lcx;
                _refY = lcy;
                break;
            case TrackingMode.SurfaceFeature:
                int w = mat.Cols;
                int h = mat.Rows;
                int rw = Math.Clamp(SurfaceRoiWidth, 16, w);
                int rh = Math.Clamp(SurfaceRoiHeight, 16, h);
                int rx = SurfaceRoiX >= 0 ? SurfaceRoiX : (w - rw) / 2;
                int ry = SurfaceRoiY >= 0 ? SurfaceRoiY : (h - rh) / 2;
                rx = Math.Clamp(rx, 0, Math.Max(0, w - rw));
                ry = Math.Clamp(ry, 0, Math.Max(0, h - rh));
                _surface.SetReferenceTemplate(mat, rx, ry, rw, rh);
                _refX = rx + rw * 0.5;
                _refY = ry + rh * 0.5;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }

        _referenceReady = true;
    }

    private void AppendHistory(GuideStep step)
    {
        lock (_sync)
        {
            _history.Add(step);
            while (_history.Count > MaxHistory)
                _history.RemoveAt(0);

            double acc = 0;
            foreach (GuideStep g in _history)
                acc += g.RaErrorArcSec * g.RaErrorArcSec + g.DecErrorArcSec * g.DecErrorArcSec;
            RmsTotal = _history.Count > 0 ? Math.Sqrt(acc / _history.Count) : 0;
        }
    }

    private static FrameData CloneFrame(FrameData f) =>
        new()
        {
            Data = (byte[])f.Data.Clone(),
            Width = f.Width,
            Height = f.Height,
            PixelFormat = f.PixelFormat,
            FrameId = f.FrameId,
            Timestamp = f.Timestamp,
            ExposureUs = f.ExposureUs,
            Gain = f.Gain,
        };
}
