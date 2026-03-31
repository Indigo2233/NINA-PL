using NINA.PL.Core;
using NINA.PL.Sequencer;

namespace NINA.PL.Sequencer.Instructions;

public sealed class WaitUntilSafeInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(WaitUntilSafeInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Safety";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public double CheckIntervalSeconds { get; set; } = 10;

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Logger.Info("Wait until safe (placeholder): sleeping {0}s", CheckIntervalSeconds);
        await Task.Delay(TimeSpan.FromSeconds(CheckIntervalSeconds), ct).ConfigureAwait(false);
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(60);
}
