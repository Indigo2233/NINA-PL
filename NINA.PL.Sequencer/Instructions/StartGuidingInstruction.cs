using NINA.PL.Core;
using NINA.PL.Guider;

namespace NINA.PL.Sequencer.Instructions;

public sealed class StartGuidingInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(StartGuidingInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Guider";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public TrackingMode TrackingMode { get; set; } = TrackingMode.DiskCentroid;

    public bool ForceCalibration { get; set; }

    public double SettlePixels { get; set; } = 1.5;

    public int SettleTimeSeconds { get; set; } = 10;

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Logger.Info("Start guiding: Mode={0}, ForceCalibration={1}, Settle={2}px/{3}s",
            TrackingMode, ForceCalibration, SettlePixels, SettleTimeSeconds);
        await context.Guider.StartGuidingAsync(context.Camera, context.Mount, TrackingMode, ct).ConfigureAwait(false);

        if (SettleTimeSeconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(SettleTimeSeconds), ct).ConfigureAwait(false);
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(2 + SettleTimeSeconds);
}
