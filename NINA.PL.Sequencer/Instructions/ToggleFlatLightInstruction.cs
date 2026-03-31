using NINA.PL.Core;

namespace NINA.PL.Sequencer.Instructions;

public sealed class ToggleFlatLightInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(ToggleFlatLightInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "FlatDevice";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public bool LightOn { get; set; }

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _ = context.FlatDevice ?? throw new InvalidOperationException("Flat device mediator not configured.");
        IFlatDeviceProvider? flat = context.FlatDevice.GetConnectedProvider()
            ?? throw new InvalidOperationException("No connected flat panel.");

        await flat.ToggleLightAsync(LightOn).ConfigureAwait(false);
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(5);
}
