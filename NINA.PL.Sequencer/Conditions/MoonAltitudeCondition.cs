using NINA.PL.Core;

namespace NINA.PL.Sequencer.Conditions;

public sealed class MoonAltitudeCondition : ISequenceCondition
{
    public string Name { get; set; } = nameof(MoonAltitudeCondition);

    public string Category { get; set; } = "Loop";

    /// <summary>Comparison: true = "moon above this", false = "moon below this".</summary>
    public bool AboveThreshold { get; set; }

    public double ThresholdAltitude { get; set; } = 0;

    public bool Check(SequenceContext context)
    {
        double alt = AstronomyUtil.MoonAltitude(DateTime.UtcNow, context.Latitude, context.Longitude);
        return AboveThreshold ? alt >= ThresholdAltitude : alt < ThresholdAltitude;
    }
}
