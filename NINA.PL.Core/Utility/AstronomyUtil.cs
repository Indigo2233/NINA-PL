namespace NINA.PL.Core;

/// <summary>
/// Lightweight Sun/Moon position calculator using simplified algorithms.
/// Good enough for sequencing decisions (±1° accuracy).
/// </summary>
public static class AstronomyUtil
{
    /// <summary>Compute the Sun's altitude (degrees) for a given UTC time and observer location.</summary>
    public static double SunAltitude(DateTime utc, double latitudeDeg, double longitudeDeg)
    {
        // Use simplified solar position algorithm
        double jd = ToJulianDate(utc);
        double n = jd - 2451545.0;
        double L = NormalizeDegrees(280.46 + 0.9856474 * n);  // mean longitude
        double g = NormalizeDegrees(357.528 + 0.9856003 * n); // mean anomaly
        double gRad = g * Math.PI / 180.0;
        double eclLong = NormalizeDegrees(L + 1.915 * Math.Sin(gRad) + 0.020 * Math.Sin(2 * gRad));
        double eclLongRad = eclLong * Math.PI / 180.0;
        double obliquity = 23.439 - 0.0000004 * n;
        double oblRad = obliquity * Math.PI / 180.0;
        double ra = Math.Atan2(Math.Cos(oblRad) * Math.Sin(eclLongRad), Math.Cos(eclLongRad));
        double dec = Math.Asin(Math.Sin(oblRad) * Math.Sin(eclLongRad));
        double gmst = NormalizeDegrees(280.46061837 + 360.98564736629 * n);
        double lmst = gmst + longitudeDeg;
        double ha = (lmst - ra * 180.0 / Math.PI) * Math.PI / 180.0;
        double latRad = latitudeDeg * Math.PI / 180.0;
        double alt = Math.Asin(Math.Sin(latRad) * Math.Sin(dec) + Math.Cos(latRad) * Math.Cos(dec) * Math.Cos(ha));
        return alt * 180.0 / Math.PI;
    }

    /// <summary>Compute the Moon's altitude (degrees) for a given UTC time and observer location (simplified).</summary>
    public static double MoonAltitude(DateTime utc, double latitudeDeg, double longitudeDeg)
    {
        double jd = ToJulianDate(utc);
        double n = jd - 2451545.0;
        double L0 = NormalizeDegrees(218.316 + 13.176396 * n);
        double M = NormalizeDegrees(134.963 + 13.064993 * n);
        double F = NormalizeDegrees(93.272 + 13.229350 * n);
        double MRad = M * Math.PI / 180.0;
        double FRad = F * Math.PI / 180.0;
        double lon = NormalizeDegrees(L0 + 6.289 * Math.Sin(MRad));
        double lat = 5.128 * Math.Sin(FRad);
        double lonRad = lon * Math.PI / 180.0;
        double latRad2 = lat * Math.PI / 180.0;
        double obliquity = 23.439 * Math.PI / 180.0;
        double ra = Math.Atan2(Math.Sin(lonRad) * Math.Cos(obliquity) - Math.Tan(latRad2) * Math.Sin(obliquity), Math.Cos(lonRad));
        double dec = Math.Asin(Math.Sin(latRad2) * Math.Cos(obliquity) + Math.Cos(latRad2) * Math.Sin(obliquity) * Math.Sin(lonRad));
        double gmst = NormalizeDegrees(280.46061837 + 360.98564736629 * n);
        double lmst = gmst + longitudeDeg;
        double ha = (lmst - ra * 180.0 / Math.PI) * Math.PI / 180.0;
        double latRad = latitudeDeg * Math.PI / 180.0;
        double alt = Math.Asin(Math.Sin(latRad) * Math.Sin(dec) + Math.Cos(latRad) * Math.Cos(dec) * Math.Cos(ha));
        return alt * 180.0 / Math.PI;
    }

    /// <summary>Moon illumination fraction (0..1).</summary>
    public static double MoonIllumination(DateTime utc)
    {
        double jd = ToJulianDate(utc);
        double n = jd - 2451545.0;
        double sunM = NormalizeDegrees(357.528 + 0.9856003 * n);
        double moonM = NormalizeDegrees(134.963 + 13.064993 * n);
        double diff = moonM - sunM;
        return (1 - Math.Cos(diff * Math.PI / 180.0)) / 2.0;
    }

    /// <summary>Local sidereal time in hours (0..24).</summary>
    public static double LocalSiderealTimeHours(DateTime utc, double longitudeDeg)
    {
        double jd = ToJulianDate(utc);
        double n = jd - 2451545.0;
        double gmst = NormalizeDegrees(280.46061837 + 360.98564736629 * n);
        double lmstDeg = NormalizeDegrees(gmst + longitudeDeg);
        return lmstDeg / 15.0;
    }

    /// <summary>Hour angle in hours (-12..12). <paramref name="rightAscensionHours"/> in ASCOM hours.</summary>
    public static double HourAngleHours(double rightAscensionHours, DateTime utc, double longitudeDeg)
    {
        double lst = LocalSiderealTimeHours(utc, longitudeDeg);
        double ha = lst - rightAscensionHours;
        if (ha > 12) ha -= 24;
        if (ha < -12) ha += 24;
        return ha;
    }

    /// <summary>
    /// Returns twilight type for current sun altitude.
    /// </summary>
    public static TwilightType GetTwilightType(double sunAltitudeDeg) =>
        sunAltitudeDeg switch
        {
            > 0 => TwilightType.Day,
            > -6 => TwilightType.Civil,
            > -12 => TwilightType.Nautical,
            > -18 => TwilightType.Astronomical,
            _ => TwilightType.Night,
        };

    private static double ToJulianDate(DateTime utc)
    {
        int y = utc.Year, m = utc.Month, d = utc.Day;
        double dayFraction = (utc.Hour + utc.Minute / 60.0 + utc.Second / 3600.0) / 24.0;
        if (m <= 2) { y--; m += 12; }
        int A = y / 100;
        int B = 2 - A + A / 4;
        return (int)(365.25 * (y + 4716)) + (int)(30.6001 * (m + 1)) + d + dayFraction + B - 1524.5;
    }

    private static double NormalizeDegrees(double deg) => ((deg % 360) + 360) % 360;
}

public enum TwilightType
{
    Day,
    Civil,
    Nautical,
    Astronomical,
    Night,
}
