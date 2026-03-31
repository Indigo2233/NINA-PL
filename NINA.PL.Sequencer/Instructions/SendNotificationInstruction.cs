using NINA.PL.Core;

namespace NINA.PL.Sequencer.Instructions;

public sealed class SendNotificationInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(SendNotificationInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Utility";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        Logger.Info("[Notification] {0}: {1}", string.IsNullOrEmpty(Title) ? "(no title)" : Title, Message);
        return Task.CompletedTask;
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(1);
}
