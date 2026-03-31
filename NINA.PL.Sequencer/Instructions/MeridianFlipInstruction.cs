using NINA.PL.Core;

namespace NINA.PL.Sequencer.Instructions;

public sealed class MeridianFlipInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(MeridianFlipInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Telescope";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public int PauseBeforeFlipSeconds { get; set; } = 30;

    public bool Recenter { get; set; } = true;

    public int SettleTimeSeconds { get; set; } = 10;

    public bool AutoFocusAfterFlip { get; set; } = true;

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        IMountProvider mount = context.Mount.GetConnectedProvider()
            ?? throw new InvalidOperationException("No connected mount.");

        double ra = mount.RightAscension;
        double dec = mount.Declination;

        if (PauseBeforeFlipSeconds > 0)
        {
            Logger.Info("Meridian flip: pausing {0}s before flip", PauseBeforeFlipSeconds);
            await Task.Delay(TimeSpan.FromSeconds(PauseBeforeFlipSeconds), ct).ConfigureAwait(false);
        }

        await mount.SetTrackingAsync(false).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        await mount.SlewToCoordinatesAsync(ra, dec).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        await mount.SetTrackingAsync(true).ConfigureAwait(false);

        if (SettleTimeSeconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(SettleTimeSeconds), ct).ConfigureAwait(false);

        Logger.Info("Meridian flip: RA={0}h, Dec={1}°, Recenter={2}, AF={3}", ra, dec, Recenter, AutoFocusAfterFlip);
    }

    public TimeSpan GetEstimatedDuration() =>
        TimeSpan.FromSeconds(60 + PauseBeforeFlipSeconds + SettleTimeSeconds);
}
