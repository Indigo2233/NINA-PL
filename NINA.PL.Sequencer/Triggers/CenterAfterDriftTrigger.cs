using NINA.PL.Core;

namespace NINA.PL.Sequencer.Triggers;

/// <summary>Placeholder: periodic re-centering after drift (threshold unused until plate-solve data exists).</summary>
public sealed class CenterAfterDriftTrigger : ISequenceTrigger
{
    public string Name { get; set; } = nameof(CenterAfterDriftTrigger);

    public string Category { get; set; } = "Telescope";

    public int AfterExposures { get; set; } = 10;

    public double ThresholdArcminutes { get; set; } = 5.0;

    private int _lastFiredAtCount = -1;

    public bool ShouldTrigger(SequenceContext context) => false;

    public bool ShouldTriggerAfter(SequenceContext context)
    {
        if (AfterExposures <= 0)
            return false;

        int n = context.CompletedExposureCount;
        if (n <= 0 || n % AfterExposures != 0)
            return false;

        if (n == _lastFiredAtCount)
            return false;

        return true;
    }

    public Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        Logger.Info("Center after drift check (placeholder)");
        _lastFiredAtCount = context.CompletedExposureCount;
        return Task.CompletedTask;
    }
}
