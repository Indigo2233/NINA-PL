using NINA.PL.Core;

namespace NINA.PL.Sequencer.Instructions;

public sealed class WaitForAltitudeInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(WaitForAltitudeInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Utility";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public double MinAltitude { get; set; } = 30;

    public double CheckIntervalSeconds { get; set; } = 10;

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            IMountProvider? mount = context.Mount.GetConnectedProvider();
            if (mount is not null && mount.Altitude >= MinAltitude)
                return;

            await Task.Delay(TimeSpan.FromSeconds(CheckIntervalSeconds), ct).ConfigureAwait(false);
        }
    }

    public TimeSpan GetEstimatedDuration() =>
        TimeSpan.FromSeconds(Math.Max(CheckIntervalSeconds * 2, 60));
}
