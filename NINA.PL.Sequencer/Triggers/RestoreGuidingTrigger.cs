using NINA.PL.Core;
using NINA.PL.Guider;

namespace NINA.PL.Sequencer.Triggers;

/// <summary>Restores planetary guiding when appropriate (placeholder gate on <see cref="ShouldTriggerAfter"/>).</summary>
public sealed class RestoreGuidingTrigger : ISequenceTrigger
{
    public string Name { get; set; } = nameof(RestoreGuidingTrigger);

    public string Category { get; set; } = "Guider";

    public bool ShouldTrigger(SequenceContext context) => false;

    public bool ShouldTriggerAfter(SequenceContext context) => false;

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        await context.Guider.StartGuidingAsync(context.Camera, context.Mount, TrackingMode.DiskCentroid, ct).ConfigureAwait(false);
    }
}
