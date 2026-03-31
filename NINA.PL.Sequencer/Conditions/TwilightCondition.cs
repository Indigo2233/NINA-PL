using NINA.PL.Core;

namespace NINA.PL.Sequencer.Conditions;

public sealed class TwilightCondition : ISequenceCondition
{
    public string Name { get; set; } = nameof(TwilightCondition);

    public string Category { get; set; } = "Loop";

    /// <summary>The twilight type that must be active for this condition to pass.</summary>
    public TwilightType RequiredTwilight { get; set; } = TwilightType.Astronomical;

    /// <summary>If true, the condition passes when twilight is at or darker than RequiredTwilight.</summary>
    public bool OrDarker { get; set; } = true;

    public bool Check(SequenceContext context)
    {
        double sunAlt = AstronomyUtil.SunAltitude(DateTime.UtcNow, context.Latitude, context.Longitude);
        TwilightType current = AstronomyUtil.GetTwilightType(sunAlt);
        if (OrDarker)
            return (int)current >= (int)RequiredTwilight;
        return current == RequiredTwilight;
    }
}
