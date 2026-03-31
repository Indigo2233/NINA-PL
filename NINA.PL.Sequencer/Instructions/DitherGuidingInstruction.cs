using NINA.PL.Core;

namespace NINA.PL.Sequencer.Instructions;

public sealed class DitherGuidingInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(DitherGuidingInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Guider";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public double DitherPixels { get; set; } = 5.0;

    public bool RAOnly { get; set; }

    public int SettleTimeSeconds { get; set; } = 5;

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        IMountProvider? mount = context.Mount.GetConnectedProvider()
            ?? throw new InvalidOperationException("No connected mount.");

        var direction = RAOnly
            ? (Random.Shared.Next(2) == 0 ? GuideDirection.West : GuideDirection.East)
            : (GuideDirection)Random.Shared.Next(0, Enum.GetValues<GuideDirection>().Length);

        int ms = (int)(DitherPixels * 100);
        await mount.PulseGuideAsync(direction, ms).ConfigureAwait(false);
        Logger.Info("Dither: {0}px, direction={1}, RAOnly={2}", DitherPixels, direction, RAOnly);

        if (SettleTimeSeconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(SettleTimeSeconds), ct).ConfigureAwait(false);
    }

    public TimeSpan GetEstimatedDuration() =>
        TimeSpan.FromSeconds(Math.Max(1, DitherPixels * 0.1 + SettleTimeSeconds));
}
