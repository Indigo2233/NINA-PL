using NINA.PL.Core;

namespace NINA.PL.Sequencer.Conditions;

public sealed class MeridianFlipCondition : ISequenceCondition
{
    public string Name { get; set; } = nameof(MeridianFlipCondition);

    public string Category { get; set; } = "Loop";

    /// <summary>Hours past meridian threshold. Default 0.5 = 30min window.</summary>
    public double HoursThreshold { get; set; } = 0.5;

    public bool Check(SequenceContext context)
    {
        IMountProvider? mount = context.Mount.GetConnectedProvider();
        if (mount is null) return false;

        // Hour angle: HA = LST - RA (ASCOM RA in hours). Near meridian when |HA| is small.
        double ha = AstronomyUtil.HourAngleHours(mount.RightAscension, DateTime.UtcNow, context.Longitude);
        return Math.Abs(ha) < HoursThreshold;
    }
}
