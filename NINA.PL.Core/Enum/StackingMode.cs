namespace NINA.PL.Core;

/// <summary>
/// Combination strategy when stacking multiple registered frames.
/// </summary>
public enum StackingMode
{
    Mean,
    Median,
    SigmaClip,
}
