using NINA.PL.Core;

namespace NINA.PL.Sequencer.Triggers;

/// <summary>Placeholder: future HFR-based autofocus trigger.</summary>
public sealed class AutofocusAfterHFRTrigger : ISequenceTrigger
{
    public string Name { get; set; } = nameof(AutofocusAfterHFRTrigger);

    public string Category { get; set; } = "Focuser";

    public double HFRIncreasePercent { get; set; } = 15;

    public int SampleSize { get; set; } = 5;

    public bool ShouldTrigger(SequenceContext context) => false;

    public bool ShouldTriggerAfter(SequenceContext context) => false;

    public Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        Logger.Info("AF after HFR increase (placeholder)");
        return Task.CompletedTask;
    }
}
