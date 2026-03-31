using NINA.PL.Core;
using NINA.PL.Sequencer;

namespace NINA.PL.Sequencer.Instructions;

public sealed class SolveAndRotateInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(SolveAndRotateInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Rotator";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public double TargetSkyAngle { get; set; }

    public double Tolerance { get; set; } = 1.0;

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        IRotatorProvider? rot = context.Rotator?.GetConnectedProvider();
        if (rot is null)
            throw new InvalidOperationException("No connected rotator.");

        ct.ThrowIfCancellationRequested();
        Logger.Info("Solve and rotate: TargetSkyAngle={0}°, Tolerance={1}°", TargetSkyAngle, Tolerance);
        await rot.MoveToAsync(TargetSkyAngle).ConfigureAwait(false);
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(30);
}
