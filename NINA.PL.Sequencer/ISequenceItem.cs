namespace NINA.PL.Sequencer;

public interface ISequenceItem
{
    string Name { get; }
    string Description { get; }
    string Category { get; }
    InstructionErrorBehavior ErrorBehavior { get; set; }
    int Attempts { get; set; }
    bool IsEnabled { get; set; }
    Task ExecuteAsync(SequenceContext context, CancellationToken ct);
    TimeSpan GetEstimatedDuration();
}
