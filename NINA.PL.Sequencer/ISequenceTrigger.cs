namespace NINA.PL.Sequencer;

public interface ISequenceTrigger
{
    string Name { get; }
    string Category { get; }
    bool ShouldTrigger(SequenceContext context);
    bool ShouldTriggerAfter(SequenceContext context) => false;
    Task ExecuteAsync(SequenceContext context, CancellationToken ct);
}
