using NINA.PL.Core;

namespace NINA.PL.Sequencer.Conditions;

/// <summary>Loop continues while the current UTC time is before a wall-clock or astronomical deadline.</summary>
public sealed class LoopUntilTimeCondition : ISequenceCondition
{
    public string Name { get; set; } = nameof(LoopUntilTimeCondition);

    public string Category { get; set; } = "Loop";

    /// <summary>Time, Sunset, CivilDusk, NauticalDusk, AstroDusk, AstroDawn, NauticalDawn, CivilDawn, Sunrise.</summary>
    public string TimeSource { get; set; } = "Time";

    public int Hours { get; set; } = 22;

    public int Minutes { get; set; } = 0;

    public int OffsetMinutes { get; set; } = 0;

    private DateTime? _deadlineUtc;

    public bool Check(SequenceContext context)
    {
        if (_deadlineUtc is null)
            _deadlineUtc = ComputeDeadlineUtc(context);

        return DateTime.UtcNow < _deadlineUtc.Value;
    }

    private DateTime ComputeDeadlineUtc(SequenceContext context)
    {
        if (string.Equals(TimeSource, "Time", StringComparison.OrdinalIgnoreCase))
            return ComputeClockDeadlineUtc(context);

        return ComputeAstronomicalDeadlineUtc(context, TimeSource);
    }

    /// <summary>Local mean solar time from longitude: target today at H:M + offset; if already passed, use tomorrow.</summary>
    private DateTime ComputeClockDeadlineUtc(SequenceContext context)
    {
        DateTime utcNow = DateTime.UtcNow;
        double lonHours = context.Longitude / 15.0;
        DateTime localMean = utcNow.AddHours(lonHours);
        DateTime localDay = localMean.Date;
        DateTime targetLocal = localDay.AddHours(Hours).AddMinutes(Minutes + OffsetMinutes);
        if (targetLocal <= localMean)
            targetLocal = targetLocal.AddDays(1);

        return DateTime.SpecifyKind(targetLocal.AddHours(-lonHours), DateTimeKind.Utc);
    }

    private static DateTime ComputeAstronomicalDeadlineUtc(SequenceContext context, string timeSource)
    {
        double lat = context.Latitude;
        double lon = context.Longitude;
        if (!TryMapTimeSource(timeSource, out double threshold, out bool isDusk))
            return DateTime.UtcNow;

        DateTime? crossing = FindNextSunCrossingUtc(DateTime.UtcNow, lat, lon, threshold, isDusk);
        return crossing ?? DateTime.UtcNow;
    }

    private static bool TryMapTimeSource(string source, out double threshold, out bool isDusk)
    {
        if (string.Equals(source, "Sunset", StringComparison.OrdinalIgnoreCase))
        {
            threshold = 0;
            isDusk = true;
            return true;
        }

        if (string.Equals(source, "CivilDusk", StringComparison.OrdinalIgnoreCase))
        {
            threshold = -6;
            isDusk = true;
            return true;
        }

        if (string.Equals(source, "NauticalDusk", StringComparison.OrdinalIgnoreCase))
        {
            threshold = -12;
            isDusk = true;
            return true;
        }

        if (string.Equals(source, "AstroDusk", StringComparison.OrdinalIgnoreCase))
        {
            threshold = -18;
            isDusk = true;
            return true;
        }

        if (string.Equals(source, "AstroDawn", StringComparison.OrdinalIgnoreCase))
        {
            threshold = -18;
            isDusk = false;
            return true;
        }

        if (string.Equals(source, "NauticalDawn", StringComparison.OrdinalIgnoreCase))
        {
            threshold = -12;
            isDusk = false;
            return true;
        }

        if (string.Equals(source, "CivilDawn", StringComparison.OrdinalIgnoreCase))
        {
            threshold = -6;
            isDusk = false;
            return true;
        }

        if (string.Equals(source, "Sunrise", StringComparison.OrdinalIgnoreCase))
        {
            threshold = 0;
            isDusk = false;
            return true;
        }

        threshold = 0;
        isDusk = true;
        return false;
    }

    /// <summary>Next UTC minute when the Sun crosses <paramref name="threshold"/> degrees (dusk: decreasing; dawn: increasing).</summary>
    private static DateTime? FindNextSunCrossingUtc(DateTime fromUtc, double latitudeDeg, double longitudeDeg, double threshold, bool isDusk)
    {
        const int maxMinutes = 72 * 60;
        double prevAlt = AstronomyUtil.SunAltitude(fromUtc, latitudeDeg, longitudeDeg);

        for (int i = 1; i <= maxMinutes; i++)
        {
            DateTime t = fromUtc.AddMinutes(i);
            double alt = AstronomyUtil.SunAltitude(t, latitudeDeg, longitudeDeg);

            if (isDusk)
            {
                if (prevAlt > threshold && alt <= threshold && alt < prevAlt)
                    return t;
            }
            else
            {
                if (prevAlt < threshold && alt >= threshold && alt > prevAlt)
                    return t;
            }

            prevAlt = alt;
        }

        return null;
    }
}
