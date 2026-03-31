using NINA.PL.Core;

namespace NINA.PL.Sequencer.Triggers;

/// <summary>Fires once when mount hour angle passes <see cref="MinutesAfterMeridian"/> minutes past the meridian.</summary>
public sealed class MeridianFlipTrigger : ISequenceTrigger
{
    public string Name { get; set; } = nameof(MeridianFlipTrigger);

    public string Category { get; set; } = "Telescope";

    public double MinutesAfterMeridian { get; set; } = 5;

    private bool _armed;

    public bool ShouldTrigger(SequenceContext context)
    {
        IMountProvider? mount = context.Mount.GetConnectedProvider();
        if (mount is null)
            return false;

        double haHours = AstronomyUtil.HourAngleHours(mount.RightAscension, DateTime.UtcNow, context.Longitude);
        double thresholdHours = MinutesAfterMeridian / 60.0;

        if (haHours < 0)
        {
            _armed = false;
            return false;
        }

        if (haHours >= thresholdHours && !_armed)
        {
            _armed = true;
            return true;
        }

        return false;
    }

    public async Task ExecuteAsync(SequenceContext context, CancellationToken ct)
    {
        IMountProvider? mount = context.Mount.GetConnectedProvider();
        if (mount is null)
        {
            Logger.Warn("MeridianFlipTrigger: skipped (no mount).");
            return;
        }

        double ra = mount.RightAscension;
        double dec = mount.Declination;

        await mount.SetTrackingAsync(false).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        await mount.SlewToCoordinatesAsync(ra, dec).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        await mount.SetTrackingAsync(true).ConfigureAwait(false);

        Logger.Info("Meridian flip trigger: re-slew to RA={0} h, Dec={1}°", ra, dec);
    }
}
