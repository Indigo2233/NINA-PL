namespace NINA.PL.Sequencer.Instructions;

/// <summary>
/// Sequence item that wraps an <see cref="ISequenceTrigger"/> for editing and placement in the tree
/// (palette parity with <see cref="ConditionInstruction"/>).
/// </summary>
public sealed class TriggerInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(TriggerInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.AbortOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public ISequenceTrigger Trigger { get; set; } = null!;

    public Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!IsEnabled)
            return Task.CompletedTask;
        return Trigger.ExecuteAsync(context, ct);
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(1);
}
