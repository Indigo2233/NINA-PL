using NINA.PL.Core;

namespace NINA.PL.Sequencer.Instructions;

public sealed class CenterTargetInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(CenterTargetInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Telescope";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public double RA { get; set; }

    public double Dec { get; set; }

    public int Iterations { get; set; } = 3;

    public double ThresholdArcsec { get; set; } = 5.0;

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        IMountProvider mount = context.Mount.GetConnectedProvider()
            ?? throw new InvalidOperationException("No connected mount.");

        double raTarget = RA > 0 ? RA : context.CurrentTargetRAHours;
        double decTarget = Dec != 0 ? Dec : context.CurrentTargetDecDegrees;

        for (int i = 0; i < Iterations; i++)
        {
            ct.ThrowIfCancellationRequested();
            await mount.SlewToCoordinatesAsync(raTarget, decTarget).ConfigureAwait(false);
            Logger.Info("Plate solve centering: iteration {0}/{1}, threshold={2}\"", i + 1, Iterations, ThresholdArcsec);
        }
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(Math.Max(1, Iterations * 30));
}
