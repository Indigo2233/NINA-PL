namespace NINA.PL.Sequencer.Conditions;

/// <summary>Placeholder: always passes until a safety monitor is integrated.</summary>
public sealed class LoopWhileSafeCondition : ISequenceCondition
{
    public string Name { get; set; } = "Loop While Safe";

    public string Category { get; set; } = "Safety";

    public bool Check(SequenceContext context) => true;
}
