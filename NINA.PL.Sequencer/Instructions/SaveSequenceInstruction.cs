using NINA.PL.Core;

namespace NINA.PL.Sequencer.Instructions;

/// <summary>Placeholder for auto-save during sequence execution.</summary>
public sealed class SaveSequenceInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(SaveSequenceInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Utility";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Logger.Info("Sequence saved (placeholder).");
        return Task.CompletedTask;
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(1);
}
