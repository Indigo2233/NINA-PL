namespace NINA.PL.Sequencer.Instructions;

public sealed class WaitInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(WaitInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Utility";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public int Seconds { get; set; }

    public Task ExecuteAsync(SequenceContext context, CancellationToken ct) =>
        Task.Delay(TimeSpan.FromSeconds(Seconds), ct);

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(Math.Max(0, Seconds));
}
