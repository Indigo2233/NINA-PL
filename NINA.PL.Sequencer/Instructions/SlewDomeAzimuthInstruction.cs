using NINA.PL.Core;
using NINA.PL.Sequencer;

namespace NINA.PL.Sequencer.Instructions;

public sealed class SlewDomeAzimuthInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(SlewDomeAzimuthInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Dome";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public double Azimuth { get; set; }

    public Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Logger.Info("Dome: slew to azimuth {0}°", Azimuth);
        return Task.CompletedTask;
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(30);
}
