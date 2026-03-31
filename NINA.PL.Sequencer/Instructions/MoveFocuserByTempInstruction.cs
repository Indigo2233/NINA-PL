using NINA.PL.Core;
using NINA.PL.Sequencer;

namespace NINA.PL.Sequencer.Instructions;

public sealed class MoveFocuserByTempInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(MoveFocuserByTempInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Focuser";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public double Slope { get; set; } = 1.0;

    public double Intercept { get; set; }

    public bool AbsoluteMode { get; set; } = true;

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        IFocuserProvider focuser = context.Focuser.GetConnectedProvider()
            ?? throw new InvalidOperationException("No connected focuser.");

        ct.ThrowIfCancellationRequested();
        double temp = focuser.Temperature;
        double computed = temp * Slope + Intercept;
        int steps = (int)Math.Round(computed);
        Logger.Info("Move focuser by temperature: Slope={0}, Intercept={1}, AbsoluteMode={2}, Temp={3}, computed={4}, steps={5}",
            Slope, Intercept, AbsoluteMode, temp, computed, steps);

        if (AbsoluteMode)
            await focuser.MoveAbsoluteAsync(steps).ConfigureAwait(false);
        else
            await focuser.MoveRelativeAsync(steps).ConfigureAwait(false);
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(15);
}
