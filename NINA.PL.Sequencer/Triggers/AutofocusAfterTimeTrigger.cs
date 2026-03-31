using NINA.PL.Core;

namespace NINA.PL.Sequencer.Triggers;

/// <summary>Runs autofocus when <see cref="IntervalMinutes"/> have elapsed since the last run (or sequence start).</summary>
public sealed class AutofocusAfterTimeTrigger : ISequenceTrigger
{
    public string Name { get; set; } = nameof(AutofocusAfterTimeTrigger);

    public string Category { get; set; } = "Autofocus";

    public double IntervalMinutes { get; set; } = 30;

    private DateTime _anchorUtc = DateTime.MinValue;

    public bool ShouldTrigger(SequenceContext context)
    {
        if (IntervalMinutes <= 0)
            return false;

        if (_anchorUtc == DateTime.MinValue)
        {
            _anchorUtc = context.SequenceStartTime != default
                ? context.SequenceStartTime
                : DateTime.UtcNow;
            return false;
        }

        return (DateTime.UtcNow - _anchorUtc).TotalMinutes >= IntervalMinutes;
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
            Logger.Warn("AutofocusAfterTimeTrigger: skipped (no focuser or camera).");
        }

        _anchorUtc = DateTime.UtcNow;
    }
}
