namespace NINA.PL.Core;

/// <summary>
/// A sampled focuser position and the focus metric measured at that position.
/// </summary>
public sealed class FocusPoint
{
    public required int Position { get; init; }

    public required double MetricValue { get; init; }

    public required FocusMetricType MetricType { get; init; }
}
