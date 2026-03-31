using NINA.PL.Core;

namespace NINA.PL.Sequencer.Instructions;

public sealed class AnnotationInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(AnnotationInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Utility";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public string Message { get; set; } = string.Empty;

    public Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        Logger.Info(Message);
        return Task.CompletedTask;
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(1);
}
