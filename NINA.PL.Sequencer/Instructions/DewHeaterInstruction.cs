using NINA.PL.Core;

namespace NINA.PL.Sequencer.Instructions;

public sealed class DewHeaterInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(DewHeaterInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Camera";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public bool TurnOn { get; set; }

    public Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Logger.Info("Dew heater: {0}", TurnOn ? "ON" : "OFF");
        return Task.CompletedTask;
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(2);
}
