using System;

namespace NINA.PL.Guider;

/// <summary>
/// Discrete-time PID with anti-windup (integral clamped when saturated) and output limiting.
/// </summary>
public sealed class PIDController
{
    private readonly double _kP;
    private readonly double _kI;
    private readonly double _kD;
    private readonly double _maxOutput;

    private double _integral;
    private double _lastError;
    private bool _hasLast;

    public PIDController(double kP, double kI, double kD, double maxOutput)
    {
        if (maxOutput <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxOutput), "maxOutput must be positive.");

        _kP = kP;
        _kI = kI;
        _kD = kD;
        _maxOutput = maxOutput;
    }

    /// <summary>
    /// Computes control output for the given error and sample interval (seconds).
    /// </summary>
    public double Compute(double error, double dt)
    {
        if (dt <= 0 || double.IsNaN(dt) || double.IsInfinity(dt))
            dt = 1e-3;

        double p = _kP * error;

        double iCandidate = _integral + error * dt;
        double d = _hasLast ? _kD * (error - _lastError) / dt : 0;

        double uUnclamped = p + _kI * iCandidate + d;
        double u = Math.Clamp(uUnclamped, -_maxOutput, _maxOutput);

        // Anti-windup: only integrate if output is not saturated in the direction of the error.
        bool saturatedHigh = u >= _maxOutput - 1e-9 && error > 0;
        bool saturatedLow = u <= -_maxOutput + 1e-9 && error < 0;
        if (!saturatedHigh && !saturatedLow)
            _integral = iCandidate;

        _lastError = error;
        _hasLast = true;
        return u;
    }

    public void Reset()
    {
        _integral = 0;
        _lastError = 0;
        _hasLast = false;
    }
}
