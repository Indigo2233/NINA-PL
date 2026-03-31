using NINA.PL.Core;

namespace NINA.PL.Sequencer.Triggers;

/// <summary>Fires after an instruction when focuser temperature drifts by at least <see cref="TemperatureChangeThreshold"/> °C from the last sample.</summary>
public sealed class AutofocusAfterTemperatureChangeTrigger : ISequenceTrigger
{
    public string Name { get; set; } = nameof(AutofocusAfterTemperatureChangeTrigger);

    public string Category { get; set; } = "Focuser";

    public double TemperatureChangeThreshold { get; set; } = 2.0;

    private double? _lastFocusTemp;

    public bool ShouldTrigger(SequenceContext context) => false;

    public bool ShouldTriggerAfter(SequenceContext context)
    {
        double currentTemp = context.Focuser.GetConnectedProvider()?.Temperature ?? 0;

        if (_lastFocusTemp is null)
        {
            _lastFocusTemp = currentTemp;
            return false;
        }

        if (Math.Abs(currentTemp - _lastFocusTemp.Value) >= TemperatureChangeThreshold)
        {
            _lastFocusTemp = currentTemp;
            return true;
        }

        return false;
    }

    public Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        Logger.Info("Autofocus after temperature change (placeholder)");
        return Task.CompletedTask;
    }
}
