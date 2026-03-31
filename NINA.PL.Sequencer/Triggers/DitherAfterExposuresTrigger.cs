using NINA.PL.Core;

namespace NINA.PL.Sequencer.Triggers;

/// <summary>Pulse-guide dither after every <see cref="ExposureCount"/> completed exposures.</summary>
public sealed class DitherAfterExposuresTrigger : ISequenceTrigger
{
    public string Name { get; set; } = nameof(DitherAfterExposuresTrigger);

    public string Category { get; set; } = "Guider";

    public int ExposureCount { get; set; } = 5;

    public int DitherAmountMs { get; set; } = 500;

    private int _lastFiredAtCount = -1;

    public bool ShouldTrigger(SequenceContext context)
    {
        if (ExposureCount <= 0)
            return false;

        int n = context.CompletedExposureCount;
        if (n <= 0 || n % ExposureCount != 0)
            return false;

        if (n == _lastFiredAtCount)
            return false;

        return true;
    }

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        IMountProvider? mount = context.Mount.GetConnectedProvider();
        if (mount is null)
        {
            Logger.Warn("DitherAfterExposuresTrigger: skipped (no mount).");
            _lastFiredAtCount = context.CompletedExposureCount;
            return;
        }

        var direction = (GuideDirection)Random.Shared.Next(0, Enum.GetValues<GuideDirection>().Length);
        await mount.PulseGuideAsync(direction, DitherAmountMs).ConfigureAwait(false);
        _lastFiredAtCount = context.CompletedExposureCount;
    }
}
