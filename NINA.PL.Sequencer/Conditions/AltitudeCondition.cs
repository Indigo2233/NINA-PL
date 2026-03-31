using NINA.PL.Core;

namespace NINA.PL.Sequencer.Conditions;

public sealed class AltitudeCondition : ISequenceCondition
{
    public string Name { get; set; } = nameof(AltitudeCondition);

    public string Category { get; set; } = "Loop";

    public double MinAltitude { get; set; }

    public bool Check(SequenceContext context)
    {
        IMountProvider? mount = context.Mount.GetConnectedProvider();
        if (mount is null)
            return false;

        return mount.Altitude >= MinAltitude;
    }
}
