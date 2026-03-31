using NINA.PL.Core;

namespace NINA.PL.Sequencer.Instructions;

public sealed class MoveRotatorInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(MoveRotatorInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Rotator";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    /// <summary>Mechanical position angle in degrees (0–360).</summary>
    public double MechanicalPosition { get; set; }

    public bool IsRelative { get; set; }

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _ = context.Rotator ?? throw new InvalidOperationException("Rotator mediator not configured.");
        IRotatorProvider? rot = context.Rotator.GetConnectedProvider()
            ?? throw new InvalidOperationException("No connected rotator.");

        Logger.Info("Rotator: position={0}°, relative={1}", MechanicalPosition, IsRelative);
        await rot.MoveToAsync(MechanicalPosition).ConfigureAwait(false);
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(20);
}
