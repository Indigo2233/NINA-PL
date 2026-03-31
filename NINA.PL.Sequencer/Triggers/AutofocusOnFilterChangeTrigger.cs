using NINA.PL.Core;

namespace NINA.PL.Sequencer.Triggers;

/// <summary>
/// After the first observation, fires when <see cref="IFilterWheelProvider.CurrentPosition"/> changes, then runs autofocus.
/// </summary>
public sealed class AutofocusOnFilterChangeTrigger : ISequenceTrigger
{
    public string Name { get; set; } = nameof(AutofocusOnFilterChangeTrigger);

    public string Category { get; set; } = "Autofocus";

    private int? _lastFilterPosition;

    public bool ShouldTrigger(SequenceContext context)
    {
        IFilterWheelProvider? wheel = context.FilterWheel.GetConnectedProvider();
        if (wheel is null)
            return false;

        int current = wheel.CurrentPosition;
        if (_lastFilterPosition is null)
        {
            _lastFilterPosition = current;
            return false;
        }

        return current != _lastFilterPosition.Value;
    }

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        IFocuserProvider? focuser = context.Focuser.GetConnectedProvider();
        ICameraProvider? camera = context.Camera.GetConnectedProvider();
        if (focuser is not null && camera is not null)
        {
            await context.AutoFocusEngine.RunAutoFocusAsync(focuser, camera, ct).ConfigureAwait(false);
        }

        IFilterWheelProvider? wheel = context.FilterWheel.GetConnectedProvider();
        if (wheel is not null)
            _lastFilterPosition = wheel.CurrentPosition;
    }
}
