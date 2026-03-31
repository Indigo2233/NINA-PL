using NINA.PL.Core;

namespace NINA.PL.Sequencer.Instructions;

public sealed class MoveFocuserAbsoluteInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(MoveFocuserAbsoluteInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Focuser";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public int Position { get; set; }

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        IFocuserProvider? focuser = context.Focuser.GetConnectedProvider()
            ?? throw new InvalidOperationException("No connected focuser.");

        await focuser.MoveAbsoluteAsync(Position).ConfigureAwait(false);
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(15);
}
