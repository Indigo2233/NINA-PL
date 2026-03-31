using NINA.PL.Core;

namespace NINA.PL.Sequencer.Instructions;

public sealed class ToggleSwitchInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(ToggleSwitchInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Switch";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public int SwitchIndex { get; set; }

    public bool State { get; set; }

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _ = context.SwitchHub ?? throw new InvalidOperationException("Switch mediator not configured.");
        ISwitchProvider? sw = context.SwitchHub.GetConnectedProvider()
            ?? throw new InvalidOperationException("No connected switch device.");

        await sw.SetSwitchAsync(SwitchIndex, State).ConfigureAwait(false);
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(5);
}
