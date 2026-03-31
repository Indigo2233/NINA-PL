namespace NINA.PL.Sequencer.Instructions;

public sealed class StopGuidingInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(StopGuidingInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Guider";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        context.Guider.StopGuiding();
        return Task.CompletedTask;
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(2);
}
