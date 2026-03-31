namespace NINA.PL.Core;

/// <summary>
/// One autoguiding measurement and the pulse correction applied (or planned).
/// </summary>
public sealed class GuideStep
{
    public required DateTime Timestamp { get; init; }

    public required double RaErrorArcSec { get; init; }

    public required double DecErrorArcSec { get; init; }

    public required double RaCorrectionMs { get; init; }

    public required double DecCorrectionMs { get; init; }

    public required double RmsArcSec { get; init; }
}
