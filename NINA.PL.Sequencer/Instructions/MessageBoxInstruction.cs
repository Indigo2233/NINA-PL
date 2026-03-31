using NINA.PL.Core;

namespace NINA.PL.Sequencer.Instructions;

public sealed class MessageBoxInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(MessageBoxInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Utility";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public string Text { get; set; } = string.Empty;

    public Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Logger.Info("[Message] {0}", Text);
        return Task.CompletedTask;
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(1);
}
