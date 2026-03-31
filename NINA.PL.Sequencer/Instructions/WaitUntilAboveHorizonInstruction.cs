using NINA.PL.Core;
using NINA.PL.Sequencer;

namespace NINA.PL.Sequencer.Instructions;

public sealed class WaitUntilAboveHorizonInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(WaitUntilAboveHorizonInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Utility";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public double RA { get; set; }

    public double Dec { get; set; }

    public double AltitudeOffset { get; set; }

    public double CheckIntervalSeconds { get; set; } = 30;

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        IMountProvider mount = context.Mount.GetConnectedProvider()
            ?? throw new InvalidOperationException("No connected mount.");

        double raTarget = RA == 0 ? context.CurrentTargetRAHours : RA;
        double decTarget = Dec == 0 ? context.CurrentTargetDecDegrees : Dec;

        await mount.SlewToCoordinatesAsync(raTarget, decTarget).ConfigureAwait(false);

        while (!ct.IsCancellationRequested)
        {
            if (mount.Altitude >= AltitudeOffset)
            {
                Logger.Info("WaitUntilAboveHorizon: mount altitude {0:F2}° >= offset {1:F2}°", mount.Altitude, AltitudeOffset);
                return;
            }

            Logger.Info("WaitUntilAboveHorizon: mount altitude {0:F2}° (need >= {1:F2}°), waiting {2}s",
                mount.Altitude, AltitudeOffset, CheckIntervalSeconds);
            await Task.Delay(TimeSpan.FromSeconds(CheckIntervalSeconds), ct).ConfigureAwait(false);
        }
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromMinutes(5);
}
