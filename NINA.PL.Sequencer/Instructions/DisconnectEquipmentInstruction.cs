using NINA.PL.Core;

namespace NINA.PL.Sequencer.Instructions;

/// <summary>Placeholder for disconnecting equipment during a sequence.</summary>
public sealed class DisconnectEquipmentInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(DisconnectEquipmentInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Utility";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Logger.Info("Disconnect equipment (placeholder): no action taken.");
        return Task.CompletedTask;
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(1);
}
