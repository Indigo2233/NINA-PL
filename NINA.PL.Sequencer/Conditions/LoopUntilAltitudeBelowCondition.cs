using NINA.PL.Core;

namespace NINA.PL.Sequencer.Conditions;

/// <summary>Loop continues while mount altitude is at or above <see cref="TargetAltitude"/> (stops once the scope goes below).</summary>
public sealed class LoopUntilAltitudeBelowCondition : ISequenceCondition
{
    public string Name { get; set; } = nameof(LoopUntilAltitudeBelowCondition);

    public string Category { get; set; } = "Sky";

    public double TargetAltitude { get; set; } = 30;

    public bool Check(SequenceContext context)
    {
        double alt = context.Mount.GetConnectedProvider()?.Altitude ?? 90;
        return alt >= TargetAltitude;
    }
}
