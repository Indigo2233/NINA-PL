using NINA.PL.Core;

namespace NINA.PL.Sequencer.Instructions;

public sealed class SetTrackingInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(SetTrackingInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Telescope";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public bool EnableTracking { get; set; } = true;

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        IMountProvider mount = context.Mount.GetConnectedProvider()
            ?? throw new InvalidOperationException("No connected mount.");

        await mount.SetTrackingAsync(EnableTracking).ConfigureAwait(false);
        Logger.Info("Mount tracking: {0}", EnableTracking ? "enabled" : "disabled");
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(5);
}
