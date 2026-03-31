using NINA.PL.Core;

namespace NINA.PL.Sequencer.Conditions;

/// <summary>Loop continues while mount altitude is above <see cref="AltitudeOffset"/>.</summary>
public sealed class LoopWhileAboveHorizonCondition : ISequenceCondition
{
    public string Name { get; set; } = nameof(LoopWhileAboveHorizonCondition);

    public string Category { get; set; } = "Sky";

    public double AltitudeOffset { get; set; } = 0;

    public bool Check(SequenceContext context)
    {
        IMountProvider? mount = context.Mount.GetConnectedProvider();
        if (mount is null)
            return false;

        return mount.Altitude > AltitudeOffset;
    }
}
