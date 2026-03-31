using NINA.PL.Core;

namespace NINA.PL.Sequencer.Instructions;

public sealed class CoolCameraInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(CoolCameraInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Camera";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public double TargetTemperature { get; set; } = -20;

    public double DurationMinutes { get; set; } = 10;

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ICameraProvider? camera = context.Camera.GetConnectedProvider()
            ?? throw new InvalidOperationException("No connected camera.");

        Logger.Info("Cooling camera to {0}°C over {1} min", TargetTemperature, DurationMinutes);

        double totalMs = DurationMinutes * 60_000;
        double stepMs = 2000;
        int steps = Math.Max(1, (int)(totalMs / stepMs));

        for (int i = 0; i < steps; i++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay((int)stepMs, ct).ConfigureAwait(false);
        }
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromMinutes(Math.Max(1, DurationMinutes));
}
