using NINA.PL.Core;

namespace NINA.PL.Sequencer.Conditions;

public sealed class SunAltitudeCondition : ISequenceCondition
{
    public string Name { get; set; } = nameof(SunAltitudeCondition);

    public string Category { get; set; } = "Loop";

    /// <summary>Comparison: true = "sun above this", false = "sun below this".</summary>
    public bool AboveThreshold { get; set; }

    public double ThresholdAltitude { get; set; } = 0;

    public bool Check(SequenceContext context)
    {
        double alt = AstronomyUtil.SunAltitude(DateTime.UtcNow, context.Latitude, context.Longitude);
        return AboveThreshold ? alt >= ThresholdAltitude : alt < ThresholdAltitude;
    }
}
