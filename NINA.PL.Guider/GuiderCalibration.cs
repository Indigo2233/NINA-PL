using System;
using System.Threading;
using System.Threading.Tasks;
using NINA.PL.Core;
using NINA.PL.Guider.TrackingAlgorithms;
using NINA.PL.Image;
using OpenCvSharp;

namespace NINA.PL.Guider;

/// <summary>
/// Maps measured pixel motion on the guide sensor to on-sky RA/Dec using calibration pulses.
/// </summary>
public sealed class GuiderCalibration
{
    /// <summary>
    /// Nominal on-sky RA rate during an East/West pulse (arcsec/s). Mounts vary; override if your driver reports guide rate.
    /// </summary>
    public double NominalRaArcSecPerSecond { get; set; } = 7.5;

    /// <summary>
    /// Nominal on-sky Dec rate during a North/South pulse (arcsec/s).
    /// </summary>
    public double NominalDecArcSecPerSecond { get; set; } = 7.5;

    public double RaPixelsPerArcSec { get; private set; }
    public double DecPixelsPerArcSec { get; private set; }
    public double PositionAngleRad { get; private set; }
    public bool IsCalibrated { get; private set; }

    private double _uxRa;
    private double _uyRa;
    private double _uxDec;
    private double _uyDec;

    private readonly DiskCentroidTracker _disk = new();

    /// <summary>
    /// Runs a four-pulse calibration sequence and builds the pixel ↔ arcsecond model.
    /// </summary>
    public async Task CalibrateAsync(
        IMountProvider mount,
        ICameraProvider camera,
        int stepMs = 2000,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mount);
        ArgumentNullException.ThrowIfNull(camera);
        if (!mount.CanPulseGuide)
            throw new InvalidOperationException("Mount does not support pulse guiding.");
        if (!camera.IsConnected)
            throw new InvalidOperationException("Camera is not connected.");
        if (stepMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(stepMs));

        IsCalibrated = false;

        var refFrame = await WaitNextFrameAsync(camera, ct).ConfigureAwait(false);
        double refX;
        double refY;
        using (Mat refMat = Debayer.ToMat(refFrame))
        {
            var (rx, ry, _) = _disk.DetectDisk(refMat);
            refX = rx;
            refY = ry;
        }

        double raArcSec = NominalRaArcSecPerSecond * (stepMs / 1000.0);
        double decArcSec = NominalDecArcSecPerSecond * (stepMs / 1000.0);

        await mount.PulseGuideAsync(GuideDirection.East, stepMs).ConfigureAwait(false);
        await SettleDelayAsync(stepMs, ct).ConfigureAwait(false);
        var eastFrame = await WaitNextFrameAsync(camera, ct).ConfigureAwait(false);
        double ex;
        double ey;
        using (Mat m = Debayer.ToMat(eastFrame))
        {
            var (cx, cy, _) = _disk.DetectDisk(m);
            ex = cx - refX;
            ey = cy - refY;
        }

        await mount.PulseGuideAsync(GuideDirection.West, stepMs).ConfigureAwait(false);
        await SettleDelayAsync(stepMs, ct).ConfigureAwait(false);
        _ = await WaitNextFrameAsync(camera, ct).ConfigureAwait(false);

        await mount.PulseGuideAsync(GuideDirection.North, stepMs).ConfigureAwait(false);
        await SettleDelayAsync(stepMs, ct).ConfigureAwait(false);
        var northFrame = await WaitNextFrameAsync(camera, ct).ConfigureAwait(false);
        double nx;
        double ny;
        using (Mat m = Debayer.ToMat(northFrame))
        {
            var (cx, cy, _) = _disk.DetectDisk(m);
            nx = cx - refX;
            ny = cy - refY;
        }

        await mount.PulseGuideAsync(GuideDirection.South, stepMs).ConfigureAwait(false);
        await SettleDelayAsync(stepMs, ct).ConfigureAwait(false);
        _ = await WaitNextFrameAsync(camera, ct).ConfigureAwait(false);

        double lenE = Math.Sqrt(ex * ex + ey * ey);
        double lenN = Math.Sqrt(nx * nx + ny * ny);
        if (lenE < 0.5 || lenN < 0.5)
            throw new InvalidOperationException("Calibration motion too small; check exposure, disk visibility, or pulse duration.");

        _uxRa = ex / lenE;
        _uyRa = ey / lenE;
        _uxDec = nx / lenN;
        _uyDec = ny / lenN;

        RaPixelsPerArcSec = lenE / raArcSec;
        DecPixelsPerArcSec = lenN / decArcSec;

        PositionAngleRad = Math.Atan2(_uyRa, _uxRa);
        IsCalibrated = true;
    }

    /// <summary>
    /// Converts a pixel offset (x right, y down) into RA/Dec sky error in arcseconds.
    /// </summary>
    public (double raArcSec, double decArcSec) PixelToArcSec(double dx, double dy)
    {
        if (!IsCalibrated)
            throw new InvalidOperationException("Calibration has not been completed.");

        // [dx,dy]ᵀ = Rap * ra * uRa + Dcp * dec * uDec  →  [ra,dec]ᵀ = M⁻¹ [dx,dy]ᵀ
        double m00 = RaPixelsPerArcSec * _uxRa;
        double m01 = DecPixelsPerArcSec * _uxDec;
        double m10 = RaPixelsPerArcSec * _uyRa;
        double m11 = DecPixelsPerArcSec * _uyDec;

        double det = m00 * m11 - m01 * m10;
        if (Math.Abs(det) < 1e-18)
            throw new InvalidOperationException("Calibration matrix is singular; RA and Dec pixel axes may be parallel.");

        double invDet = 1.0 / det;
        double raArcSec = invDet * (m11 * dx - m01 * dy);
        double decArcSec = invDet * (-m10 * dx + m00 * dy);
        return (raArcSec, decArcSec);
    }

    private static async Task SettleDelayAsync(int afterPulseMs, CancellationToken ct)
    {
        int ms = Math.Clamp(afterPulseMs / 4, 50, 500);
        await Task.Delay(ms, ct).ConfigureAwait(false);
    }

    private static async Task<FrameData> WaitNextFrameAsync(ICameraProvider camera, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<FrameData>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? sender, FrameData e)
        {
            camera.FrameReceived -= Handler;
            tcs.TrySetResult(e);
        }

        camera.FrameReceived += Handler;
        using (ct.Register(() => tcs.TrySetCanceled(ct)))
        {
            try
            {
                return await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                camera.FrameReceived -= Handler;
            }
        }
    }
}
