using NINA.PL.Core;
using NINA.PL.Sequencer;

namespace NINA.PL.Sequencer.Instructions;

public sealed class SynchronizeDomeInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(SynchronizeDomeInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Dome";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Logger.Info("Dome: synchronize with mount");
        return Task.CompletedTask;
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(5);
}
