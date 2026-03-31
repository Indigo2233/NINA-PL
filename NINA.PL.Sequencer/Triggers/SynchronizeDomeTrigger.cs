using NINA.PL.Core;

namespace NINA.PL.Sequencer.Triggers;

/// <summary>Placeholder: sync dome azimuth/slave state after each instruction.</summary>
public sealed class SynchronizeDomeTrigger : ISequenceTrigger
{
    public string Name { get; set; } = nameof(SynchronizeDomeTrigger);

    public string Category { get; set; } = "Dome";

    public bool ShouldTrigger(SequenceContext context) => false;

    public bool ShouldTriggerAfter(SequenceContext context) => true;

    public Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        Logger.Info("Dome sync (placeholder)");
        return Task.CompletedTask;
    }
}
