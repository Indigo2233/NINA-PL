using NINA.PL.Core;

namespace NINA.PL.Sequencer.Instructions;

public sealed class SetSwitchValueInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(SetSwitchValueInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Switch";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public int SwitchIndex { get; set; }

    public double Value { get; set; }

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _ = context.SwitchHub ?? throw new InvalidOperationException("Switch mediator not configured.");
        ISwitchProvider? sw = context.SwitchHub.GetConnectedProvider()
            ?? throw new InvalidOperationException("No connected switch device.");

        await sw.SetSwitchValueAsync(SwitchIndex, Value).ConfigureAwait(false);
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(5);
}
