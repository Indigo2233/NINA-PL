using NINA.PL.Core;
using NINA.PL.Sequencer;

namespace NINA.PL.Sequencer.Instructions;

public sealed class EnableDomeSyncInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(EnableDomeSyncInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Dome";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public bool Enable { get; set; } = true;

    public Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Logger.Info("Dome: sync {0}", Enable ? "enabled" : "disabled");
        return Task.CompletedTask;
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(2);
}
