using NINA.PL.Core;

namespace NINA.PL.Sequencer.Triggers;

/// <summary>Runs autofocus after every <see cref="ExposureCount"/> completed exposures.</summary>
public sealed class AutofocusAfterExposuresTrigger : ISequenceTrigger
{
    public string Name { get; set; } = nameof(AutofocusAfterExposuresTrigger);

    public string Category { get; set; } = "Autofocus";

    public int ExposureCount { get; set; } = 10;

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
        IFocuserProvider? focuser = context.Focuser.GetConnectedProvider();
        ICameraProvider? camera = context.Camera.GetConnectedProvider();
        if (focuser is not null && camera is not null)
        {
            await context.AutoFocusEngine.RunAutoFocusAsync(focuser, camera, ct).ConfigureAwait(false);
        }
        else
        {
            Logger.Warn("AutofocusAfterExposuresTrigger: skipped (no focuser or camera).");
        }

        _lastFiredAtCount = context.CompletedExposureCount;
    }
}
