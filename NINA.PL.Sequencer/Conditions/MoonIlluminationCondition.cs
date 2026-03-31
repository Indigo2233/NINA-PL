using NINA.PL.Core;

namespace NINA.PL.Sequencer.Conditions;

public sealed class MoonIlluminationCondition : ISequenceCondition
{
    public string Name { get; set; } = nameof(MoonIlluminationCondition);

    public string Category { get; set; } = "Loop";

    public double MaxIllumination { get; set; } = 0.5;

    public bool Check(SequenceContext context)
    {
        return AstronomyUtil.MoonIllumination(DateTime.UtcNow) <= MaxIllumination;
    }
}
