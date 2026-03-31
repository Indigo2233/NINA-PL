using NINA.PL.AutoFocus;
using NINA.PL.Capture;
using NINA.PL.Core;
using NINA.PL.Guider;

namespace NINA.PL.Sequencer;

/// <summary>
/// Shared state and services for sequence execution.
/// </summary>
public sealed class SequenceContext
{
    public required CameraMediator Camera { get; init; }
    public required MountMediator Mount { get; init; }
    public required FocuserMediator Focuser { get; init; }
    public required FilterWheelMediator FilterWheel { get; init; }
    public required CaptureEngine CaptureEngine { get; init; }
    public required AutoFocusEngine AutoFocusEngine { get; init; }
    public required PlanetaryGuider Guider { get; init; }

    public FlatDeviceMediator? FlatDevice { get; init; }

    public SwitchMediator? SwitchHub { get; init; }

    public RotatorMediator? Rotator { get; init; }

    /// <summary>Observer latitude in degrees (positive north).</summary>
    public double Latitude { get; init; }

    /// <summary>Observer longitude in degrees (positive east).</summary>
    public double Longitude { get; init; }

    public int CurrentLoopIteration { get; set; }

    public DateTime SequenceStartTime { get; set; }

    /// <summary>Increments when a take-exposure step completes successfully.</summary>
    public int CompletedExposureCount { get; set; }

    /// <summary>Current DSO / target name when running inside a DSO container.</summary>
    public string? CurrentTargetName { get; set; }

    /// <summary>Target RA in hours (context for nested instructions).</summary>
    public double CurrentTargetRAHours { get; set; }

    /// <summary>Target declination in degrees.</summary>
    public double CurrentTargetDecDegrees { get; set; }

    /// <summary>Field rotation / position angle in degrees.</summary>
    public double CurrentPositionAngleDegrees { get; set; }

    public Dictionary<string, object> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised immediately before an item's <see cref="ISequenceItem.ExecuteAsync"/> runs.</summary>
    public event EventHandler<ISequenceItem>? ItemExecutionStarted;

    /// <summary>Raised after an item's <see cref="ISequenceItem.ExecuteAsync"/> completes (success or failure).</summary>
    public event EventHandler<ISequenceItem>? ItemExecutionCompleted;

    internal void RaiseItemStarted(ISequenceItem item) =>
        ItemExecutionStarted?.Invoke(this, item);

    internal void RaiseItemCompleted(ISequenceItem item) =>
        ItemExecutionCompleted?.Invoke(this, item);
}
