namespace NINA.PL.Sequencer.Conditions;

/// <summary>Placeholder: always fails (inverse of a future safety gate).</summary>
public sealed class LoopWhileUnsafeCondition : ISequenceCondition
{
    public string Name { get; set; } = "Loop While Unsafe";

    public string Category { get; set; } = "Safety";

    public bool Check(SequenceContext context) => false;
}
