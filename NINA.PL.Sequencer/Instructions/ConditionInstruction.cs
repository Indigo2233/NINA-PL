namespace NINA.PL.Sequencer.Instructions;

/// <summary>
/// Runs a gate that succeeds only when the nested <see cref="ISequenceCondition"/> passes.
/// </summary>
public sealed class ConditionInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(ConditionInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Condition";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public ISequenceCondition Condition { get; set; } = null!;

    public Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!Condition.Check(context))
            throw new InvalidOperationException($"Condition not satisfied: {Condition.Name}");
        return Task.CompletedTask;
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(1);
}
