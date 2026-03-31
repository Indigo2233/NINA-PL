using NINA.PL.Core;
using NINA.PL.Sequencer;

namespace NINA.PL.Sequencer.Instructions;

public sealed class SlewCenterRotateInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(SlewCenterRotateInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Telescope";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public double RA { get; set; }

    public double Dec { get; set; }

    public double PositionAngle { get; set; }

    public int Iterations { get; set; } = 3;

    public double ThresholdArcsec { get; set; } = 5;

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        IMountProvider mount = context.Mount.GetConnectedProvider()
            ?? throw new InvalidOperationException("No connected mount.");

        double raTarget = RA == 0 ? context.CurrentTargetRAHours : RA;
        double decTarget = Dec == 0 ? context.CurrentTargetDecDegrees : Dec;

        for (int i = 0; i < Iterations; i++)
        {
            ct.ThrowIfCancellationRequested();
            await mount.SlewToCoordinatesAsync(raTarget, decTarget).ConfigureAwait(false);
            Logger.Info("Plate solve centering: iteration {0}/{1}, threshold={2}\"", i + 1, Iterations, ThresholdArcsec);

            IRotatorProvider? rot = context.Rotator?.GetConnectedProvider();
            if (rot is not null)
                await rot.MoveToAsync(PositionAngle).ConfigureAwait(false);
        }
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(Math.Max(1, Iterations * 30 + 20));
}
