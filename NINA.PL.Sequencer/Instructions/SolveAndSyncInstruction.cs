using NINA.PL.Core;
using NINA.PL.Sequencer;

namespace NINA.PL.Sequencer.Instructions;

public sealed class SolveAndSyncInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(SolveAndSyncInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Telescope";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Logger.Info("Solve and sync (placeholder)");
        return Task.CompletedTask;
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(30);
}
