using NINA.PL.Core;
using NINA.PL.Guider;
using NINA.PL.Sequencer;

namespace NINA.PL.Sequencer.Instructions;

public sealed class RestoreGuidingInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(RestoreGuidingInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Guider";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Logger.Info("Restore guiding: TrackingMode={0}", TrackingMode.DiskCentroid);
        await context.Guider.StartGuidingAsync(context.Camera, context.Mount, TrackingMode.DiskCentroid, ct).ConfigureAwait(false);
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(5);
}
