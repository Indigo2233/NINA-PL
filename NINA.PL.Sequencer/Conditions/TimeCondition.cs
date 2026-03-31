namespace NINA.PL.Sequencer.Conditions;

public sealed class TimeCondition : ISequenceCondition
{
    public string Name { get; set; } = nameof(TimeCondition);

    public string Category { get; set; } = "Loop";

    public TimeSpan MaxDuration { get; set; }

    public bool Check(SequenceContext context) =>
        DateTime.UtcNow - context.SequenceStartTime < MaxDuration;
}
