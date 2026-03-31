namespace NINA.PL.Sequencer.Conditions;

public sealed class LoopCondition : ISequenceCondition
{
    public string Name { get; set; } = nameof(LoopCondition);

    public string Category { get; set; } = "Loop";

    public int MaxIterations { get; set; }

    public bool Check(SequenceContext context) => context.CurrentLoopIteration < MaxIterations;
}
