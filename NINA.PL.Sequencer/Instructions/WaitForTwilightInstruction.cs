using NINA.PL.Core;

namespace NINA.PL.Sequencer.Instructions;

public sealed class WaitForTwilightInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(WaitForTwilightInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Utility";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public TwilightType TargetTwilight { get; set; } = TwilightType.Astronomical;

    public bool OrDarker { get; set; } = true;

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            double sunAlt = AstronomyUtil.SunAltitude(DateTime.UtcNow, context.Latitude, context.Longitude);
            TwilightType current = AstronomyUtil.GetTwilightType(sunAlt);
            bool ok = OrDarker ? (int)current >= (int)TargetTwilight : current == TargetTwilight;
            if (ok)
                return;

            await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
        }
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromMinutes(5);
}
