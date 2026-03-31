using NINA.PL.Core;

namespace NINA.PL.Sequencer.Instructions;

public sealed class SlewInstruction : ISequenceItem
{
    public string Name { get; set; } = nameof(SlewInstruction);

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "Telescope";

    public InstructionErrorBehavior ErrorBehavior { get; set; } = InstructionErrorBehavior.ContinueOnError;

    public int Attempts { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    /// <summary>Right ascension in hours (0–24).</summary>
    public double RA { get; set; }

    /// <summary>Declination in degrees (-90 to +90).</summary>
    public double Dec { get; set; }

    public bool Rotate { get; set; }

    public double PositionAngle { get; set; }

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        IMountProvider? mount = context.Mount.GetConnectedProvider()
            ?? throw new InvalidOperationException("No connected mount.");

        double raTarget = RA > 0 ? RA : context.CurrentTargetRAHours;
        double decTarget = Dec != 0 ? Dec : context.CurrentTargetDecDegrees;

        Logger.Info("Slew: RA={0}h, Dec={1}°, Rotate={2}, PA={3}°", raTarget, decTarget, Rotate, PositionAngle);
        await mount.SlewToCoordinatesAsync(raTarget, decTarget).ConfigureAwait(false);

        if (Rotate && context.Rotator is not null)
        {
            IRotatorProvider? rot = context.Rotator.GetConnectedProvider();
            if (rot is not null)
                await rot.MoveToAsync(PositionAngle).ConfigureAwait(false);
        }
    }

    public TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(30);
}
